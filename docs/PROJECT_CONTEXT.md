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
- `Driver`: employee work role included in the driver roster.
- `InStore`: employee work role included with managers in the inside roster.

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
- hourly pay rate (`HourlyRate`, default `14.50`, editable from `0` to `10000` with up to two decimal places)

Role-specific information is kept in one-to-one profiles:

- `DriverProfile`: target weekly hours and `DriverType` (`Car`, `Moped`, `EBike`, default `Car`). Car and moped drivers can work alone. Any number of e-bike drivers requires at least one car or moped driver alongside them throughout their shifts.
- `InStoreProfile`: inside weekly target hours (default `20`).
- `ManagerProfile`: inside weekly target hours (default `20`) and eligibility to provide manager coverage.

Use profile entities for future role-specific fields. Do not add driver-only, in-store-only, or manager-only nullable columns to `Employee`.

The admin employee editor can create/update roles and profile data. It may display driver details for administrators. The driver workspace must not display driver type or target hours; it only provides availability and holiday workflows.

Hourly pay is shared employee information rather than a demand setting. Existing employees receive the `14.50` default through migration. Omitted pay rates preserve an existing employee's rate when older clients update a profile; new employees use the default.

The employee API accepts `insideTargetHours` separately from driver `targetHours`. Employees with both manager and in-store profiles use the manager target for inside fairness; saving an employee applies the inside target to both profiles. Availability and saved roster views group **Drivers** separately from **In-store & managers**. An employee with an inside role belongs to the inside group even if they also have a driver role. Active Identity roles, as well as profiles, determine generation eligibility.

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

- `Admin` or `Manager`: management console, employee management, demand, roster, and holiday administration as authorized by the API. Linked managers also have a **My availability** tab for their own weekly availability;
- `Driver`: availability and holiday tabs only;
- `InStore`: in-store availability and holiday tabs;
- other employee: shared availability and holiday workspace.

The UI must not be treated as an authorization boundary. All data access still goes through protected API endpoints.

Management permissions are role-scoped: managers can view the full availability roster, saved rosters, and demand plans, and can approve holiday requests. Demand imports/edits/deletes, roster generation/cancellation/settings, saved-roster edits, another employee's individual availability, and holiday CSV export require administrator access. Drivers, in-store staff, and managers can read and replace their own availability for the next three weeks through `GET/PUT /api/employees/{employeeId}/schedule`; the API verifies the linked employee ID and active status. Selecting someone in the management employee directory never changes the manager's personal availability target. The React controls mirror these restrictions, but the API and roster-generation hub enforce them independently.

`frontend/nginx.conf` proxies `/api/` and `/hubs/` to the backend and serves the SPA for all other paths. Keep frontend API calls same-origin and use `credentials: 'include'` for Identity cookies.

## Demand and labour

The single reusable weekly demand template retains both pizzas and deliveries from each weekday's imported pair. Admins may edit those counts, staff demands, productivity settings and sales targets. Managers have read-only access. Demand shows workload, required staff hours and sales targets; labour calculations appear with saved rosters.

- `DeliveriesPerDriverHour` defaults to `2.7`; `PizzasPerInsideHour` defaults to `20`. Both are editable from `0.01` to `1000`.
- Calculated staff demand rounds workload/productivity **up** to whole employees. Inside demand includes at least one employee per open hour, including zero-pizza hours; the inside solver requires that employee to be a manager.
- Missing pizza counts remain unknown. Existing demand templates need pizza counts entered or reimported before inside generation; omitted legacy data is not treated as zero pizzas.
- Changing productivity or explicitly recalculating replaces staff-demand overrides; an ordinary save can retain explicit manual staff counts. Older request payloads preserve omitted workload/settings fields.
- Saved-roster labour uses actual persisted shifts and each employee's current hourly pay rate. Drivers and in-store staff receive a 25% premium for hours worked on Sunday; managers keep their normal rate. Overnight shifts split at calendar midnight for Sunday pay, while totals remain assigned to the shift's business day for comparison with sales targets.
- `GET /api/admin/roster/labour?weekStart=YYYY-MM-DD` returns daily and weekly hours, cost and percentage of target sales for drivers, in-store staff with managers, and both teams combined. It reads both roster kinds for that Monday and the current reusable demand template's weekday sales targets. Admins and managers may read it.
- Labour totals refresh after roster edits and are recalculated from current employee rates whenever loaded, including for older rosters. Missing team rosters are identified explicitly; a combined subtotal remains partial until both are saved. Missing or zero sales targets show no percentage. The Sunday multiplier retains full decimal precision until labour costs are rounded to cents.

## Roster generation invariants

Roster generation uses OR-Tools CP-SAT and is coordinated by the roster services/timer. Important rules include:

- driver rosters use active driver employees who do not have an inside/manager work role; inside rosters use active in-store employees and managers with the corresponding profiles;
- generated shifts are continuous and 3–10 hours, inside availability;
- exact hourly demand for the selected roster kind is required;
- at least one `Car` or `Moped` driver must cover each staffed hour; multiple `EBike` drivers cannot substitute for that support;
- inside rosters require at least one `Manager` during every configured open hour, including hours with zero pizza demand;
- minimum/preferred rest, latest start, fairness, and rolling history are enforced or scored according to saved settings;
- generation and saving are protected against concurrent jobs and stale input;
- failed or cancelled generation must not replace the saved roster.

`RosterPlan.RosterKind` is `drivers` or `inside`, defaulting old records to `drivers`. The unique key is `(WeekStart, RosterKind)`; regenerating or editing one kind preserves the other. Read, summary, history, job-log, generation and cancellation endpoints accept the `rosterKind` query parameter (default `drivers`), and saved-roster PUT accepts `rosterKind` in its body. Generation remains admin-only, with one server worker and one database lock shared by both kinds. WebSocket messages include `rosterKind`, and the frontend separates job replay by both week and kind. History uses only the matching kind; adjacent-week shifts and any shifts in the other same-week roster are checked for overlap/rest conflicts. New saved employee snapshots include roles so managers can view correct groupings without loading the admin employee editor.

Fairness uses the previous four saved weeks for both hours versus targets and average shift duration. Historical duration is total scheduled hours divided by actual shift count, pooled across valid saved shifts (not an average of weekly averages). Missing or inconsistent legacy shift details do not contribute to duration history. The independent `HistoryShiftLengthWeight` preference defaults to `100`; administrators can set it from `0` (disabled) to `1000`. It prefers a duration of `clamp(7 + group historical average - employee historical average, 6, 8)` hours for employees with valid history, using a squared duration penalty. This gives drivers with shorter past shifts a stronger preference for longer shifts, while exact demand, availability, supervision and rest remain mandatory and target-hour fairness remains separately weighted. The solver precomputes eight costs per employee, adding no search variables for this preference.

Manual roster edits validate availability, shift rules, rest and e-bike support on the server. Hourly demand-count mismatches are saved as warnings and highlighted in the saved roster; generated rosters still require exact coverage. The saved shift tables lead the page, and the historical fairness details are collapsed by default.

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
