# Project context

This document is the detailed project reference for Codex and contributors. Keep it aligned with the code when architecture or operational behavior changes.

## Repository layout

```text
Roaster Generator/              ASP.NET Core API and domain code
  Controllers/                  HTTP endpoints
  Contracts/                    API request/response models
  Data/                         AppDbContext, configurations, migrations
  Entities/                     EF Core entities
  Hubs/                         SignalR roster-generation hub
  Security/                     role and authorization policy names
  Services/                     application/domain services and identity seeding
  Validation/                   FluentValidation validators
Roaster Generator.Tests/        .NET solver and roster tests
frontend/                       React/Vite application and Nginx reverse proxy
.github/workflows/deploy.yml    GHCR build plus SSH deployment to the VPS
compose.yaml                    local full stack
compose.prod.yaml               production stack behind Traefik
```

## Runtime and deployment

The backend targets .NET 10 and listens on port `8080` in Docker. PostgreSQL is the `postgres` service and the backend connects to it using the `Postgres` connection string. The frontend listens on port `80` inside its container and serves the React bundle.

The production host is configured by `/opt/roaster-generator/.env` and `compose.prod.yaml`. The workflow publishes:

- `ghcr.io/volodymyrvolynets/roaster-generator-dominos:latest`
- `ghcr.io/volodymyrvolynets/roaster-frontend:latest`

The workflow uploads `compose.prod.yaml`, writes PostgreSQL/admin environment values from GitHub Secrets, pulls images, and runs `docker compose up -d --remove-orphans`. Database migrations run when the backend starts. A code change is not deployed until the relevant commit reaches the workflow branch and the image has been rebuilt and pulled.

Do not remove the `postgres_data` or `data_protection_keys` volumes in production. The data-protection volume preserves ASP.NET Identity cookie keys across backend container replacement. The backend also processes `X-Forwarded-Proto`/`X-Forwarded-For` from Traefik/Nginx.

## Identity, users, and roles

`ApplicationUser` extends ASP.NET Core Identity and has an optional `EmployeeId` link. The administrator is a standalone Identity user. Employee users are linked to `Employee` and use the employee number as their username.

Roles:

- `Admin`: system administrator. Can manage employee accounts, grant/remove roles, administer holidays, and use the management console.
- `Manager`: employee work role with management-console access through `ManagerAccess` (`Admin` or `Manager`).
- `Driver`: employee work role included in roster generation.
- `InStore`: employee work role for in-store employees.

Every employee needs at least one work role. Existing legacy `User` roles are removed by the identity seeder; employees without a work role are assigned `Driver`. Newly created employee passwords are `12345`. The configured admin password is taken from `Auth__AdminPassword`. Existing passwords are intentionally preserved on normal startup.

The login flow is:

1. `POST /api/auth/login` finds the username, rejects missing/inactive employees, checks the Identity password, and issues the `roaster-generator-auth` application cookie.
2. `GET /api/auth/me` validates the cookie, reloads the Identity user and linked active employee, and returns username, roles, admin status, employee ID, and employee display name.
3. Employee/admin controllers apply API authorization policies and ownership checks independently of the React UI.

If existing credentials are unknown, the opt-in `RESET_ADMIN_PASSWORD_ON_STARTUP` and `RESET_EMPLOYEE_PASSWORDS_ON_STARTUP` environment switches can be used once. The former sets the admin password to `ADMIN_PASSWORD`; the latter sets existing employee passwords to `12345`. Disable both immediately after the reset.

## Employee model

`Employee` stores shared fields:

- employee number
- first name
- last name
- phone number
- optional payroll number
- active/inactive status

Role-specific information is kept in one-to-one profiles:

- `DriverProfile`: target weekly hours and `DriverType` (`Car`, `Moped`, `EBike`, default `Car`). Car drivers can cover a staffed hour alone; other driver types require a car driver alongside them.
- `InStoreProfile`: currently empty placeholder.
- `ManagerProfile`: currently empty placeholder.

Use profile entities for future role-specific fields. Do not add driver-only, in-store-only, or manager-only nullable columns to `Employee`.

The admin employee editor can create/update roles and profile data. It may display driver details for administrators. The driver workspace must not display driver type or target hours; it only provides availability and holiday workflows.

## Holidays

`HolidayRequest` belongs to an employee and stores requested hours plus request/used timestamps and status. The rules are:

- maximum `80` hours per request;
- each employee can have only one pending/active request;
- the employee can create, edit, or delete their own pending request;
- after admin approval, status becomes `Used` and the record is read-only history;
- employees see only their own requested and used records;
- admins see all pending and used records, can approve one/all pending requests, and can export CSV.

Endpoints:

- employee: `GET/POST /api/holidays`, `PUT/DELETE /api/holidays/{id}`;
- admin: `GET /api/admin/holidays`, `GET /api/admin/holidays/export`, `POST /api/admin/holidays/{id}/approve`, `POST /api/admin/holidays/approve-all`.

Enforce these rules in the API and validator/service layer; UI limits are only convenience checks.

## Frontend workspaces

`frontend/src/App.jsx` selects the workspace from the roles returned by `/api/auth/me`:

- `Admin` or `Manager`: management console, employee management, demand, roster, and holiday administration as authorized by the API;
- `Driver`: availability and holiday tabs only;
- `InStore`: in-store availability and holiday tabs;
- other employee: shared availability and holiday workspace.

The UI must not be treated as an authorization boundary. All data access still goes through protected API endpoints.

`frontend/nginx.conf` proxies `/api/` and `/hubs/` to the backend and serves the SPA for all other paths. Keep frontend API calls same-origin and use `credentials: 'include'` for Identity cookies.

## Roster generation invariants

Roster generation uses OR-Tools CP-SAT and is coordinated by the roster services/timer. Important rules include:

- only active drivers with driver profiles are roster inputs;
- generated shifts are continuous and 3–10 hours, inside availability;
- exact hourly driver demand is required where demand is staffed;
- at least one `Car` driver must cover each staffed hour;
- minimum/preferred rest, latest start, fairness, and rolling history are enforced or scored according to saved settings;
- generation and saving are protected against concurrent jobs and stale input;
- failed or cancelled generation must not replace the saved roster.

Do not change solver constraints or scoring casually while fixing unrelated API/UI behavior. Update the relevant tests when changing scheduling rules.

## Validation and verification

Request validation belongs in `Roaster Generator/Validation/` and is registered in `Program.cs`. Controllers should return consistent validation problem details and leave domain calculations to services.

Useful checks:

```bash
dotnet build "Roaster Generator/Roaster Generator.csproj" -c Release --no-restore
dotnet test "Roaster Generator.Tests/Roaster Generator.Tests.csproj" -c Release --no-restore
docker compose -f compose.yaml config
docker compose -f compose.yaml up -d --build
```

For authentication debugging, check backend logs without printing secrets, then test login and `/api/auth/me` separately. A `401` from `/api/auth/login` means username/password or employee active state; a `401` from `/api/auth/me` after successful login means the cookie was not accepted, could not be decrypted, was not sent through the proxy, or the linked user/employee became invalid.
