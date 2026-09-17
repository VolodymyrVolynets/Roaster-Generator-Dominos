# Project context

This document is the detailed project reference for Codex and contributors. Keep it aligned with the code when architecture or operational behavior changes.

## Repository layout

```text
Roaster Generator/              ASP.NET Core API and domain code
  Controllers/                  HTTP endpoints
  Contracts/                    API request/response models
  Data/                         AppDbContext, configurations, migrations
  Entities/                     EF Core entities
  Hubs/                         SignalR generation-progress and application-change hubs
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
- `InStore`: employee work role with personal availability and holiday access; included in the manually created inside roster.

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
- maximum weekly approximate hours (`MaximumWeeklyHours`, default `45`, editable from `0` to `168` whole hours for every role)

Role-specific information is kept in one-to-one profiles:

- `DriverProfile`: `DriverType` (`Car`, `Moped`, `EBike`, default `Car`), `IsOwn` (default `true`), plus a legacy target-hours column that is no longer exposed, edited, or used for generation. `IsOwn = false` means the driver needs a company vehicle of their driver type; own vehicles do not consume company capacity. Existing drivers migrate to own vehicles, omitted ownership preserves the current value on updates, and explicit `false` must persist. Car and moped drivers can work alone. Any number of e-bike drivers requires at least one car or moped driver alongside them throughout their shifts.
- `InStoreProfile`: retained in-store role data, including an unused legacy target-hours column.
- `ManagerProfile`: retained manager role data, including an unused legacy target-hours column.

Use profile entities for future role-specific fields. Do not add driver-only, in-store-only, or manager-only nullable columns to `Employee`.

The admin employee editor can create/update roles, driver type and own/company vehicle ownership. Driver profile details, including ownership, remain management-only. Manual target hours have been removed from employee requests, responses, validation, active roster contracts, and the interface. Existing profile columns remain only for database-history compatibility and must not influence new rosters. Pre-automatic-hours roster snapshots are readable, but their manual target values are explicitly excluded from fairness history.

Hourly pay is shared employee information rather than a demand setting. Existing employees receive the `14.50` default through migration. Omitted pay rates preserve an existing employee's rate when older clients update a profile; new employees use the default.

Maximum weekly hours is also shared employee information, editable in management employee views. Existing and new employees default to `45`; omitted limits preserve an existing value on updates. An explicit `0` is preserved and gives that employee zero approximate hours. This setting caps the automatic estimate, not the actual saved or generated roster: demand coverage remains higher priority than the approximate-hours soft objective. Both driver and independent inside estimates use the employee-specific value. Changes to this setting refresh estimates and affect roster input fingerprints.

In-store staff and managers are scheduled in a separate manually created inside roster. Active matching profiles and Identity roles determine roster eligibility. Managers appear before in-store employees and satisfy the manager-supervision rule; employees with an in-store or manager role/profile remain excluded from driver generation even if they also have a driver role. Personal availability remains available to all active employees.

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

## Sick leave

`SickLeaveRequest` belongs to an employee and stores an inclusive start/finish date, review status and a required private sick-note attachment. Employees submit PDF, PNG, JPEG, GIF, WebP or BMP files up to 10 MB. The server verifies the file signature as well as the extension; attachments are never exposed as static files.

- employees can list, submit, download and remove only their own pending requests through `/api/sick-leave`;
- managers and administrators list and review requests through `/api/admin/sick-leave`;
- approval or rejection atomically clears the attachment bytes and all attachment metadata while retaining dates, reviewer, decision and timestamps as history;
- requested or approved date ranges for the same employee may not overlap;
- approved sick-leave dates remove that driver's entered availability from roster generation and saved-roster validation; pending or rejected requests do not affect scheduling.

Sick-note contents are stored temporarily in PostgreSQL so review and permanent deletion happen in the same database transaction. Treat the contents as confidential medical information and do not include them in logs or general employee responses.

Enforce these rules in the API and validator/service layer; UI limits are only convenience checks.

## Frontend workspaces

`frontend/src/App.jsx` selects the workspace from the roles returned by `/api/auth/me`:

- `Admin` or `Manager`: management console, employee management, demand, roster, and holiday administration as authorized by the API. Linked managers also have a **My availability** tab for their own weekly availability;
- `Driver`: availability, personal roster, holiday, and sick-leave tabs. The personal roster endpoint and view expose only the signed-in active driver's shifts and no other employee or driver-profile details;
- `InStore`: in-store availability and holiday tabs;
- other employee: shared availability and holiday workspace.

The UI must not be treated as an authorization boundary. All data access still goes through protected API endpoints. The workspace toolbar owns one global week selector, so the chosen week remains selected while moving between availability, demand, generation, and saved-roster tabs. It is temporarily disabled while a week-specific save, roster edit, or visible generation job is in progress. Administrators and managers can inspect any valid availability week in management views; weeks outside the next-three-week edit horizon are read-only.

Management permissions are role-scoped: managers can view the driver availability roster, saved driver and inside rosters, and demand plans, and can approve holiday requests. Demand imports/edits/deletes, driver roster generation/cancellation/settings, saved-roster creation/edits, another employee's individual availability, and holiday CSV export require administrator access. Drivers and in-store staff can read and replace their own availability for the next three weeks through `GET/PUT /api/employees/{employeeId}/schedule`; managers can read their own historical weeks but retain the same three-week write window. The API verifies the linked employee ID and active status. From Saturday 00:00 in the `Europe/Dublin` time zone, non-admin employees may view next week but can only edit from the week after next onward. System administrators retain the full next-week-through-three-weeks-ahead edit range. Schedule responses expose this server-calculated edit state so React disables locked controls, while the API independently rejects a locked write. Selecting someone in the management employee directory never changes the manager's personal availability target. The React controls mirror these restrictions, but the API and roster-generation hub enforce them independently.

Eligible drivers' weekly schedule responses and the management availability overview include an aggregate outside-demand heatmap. Each positive-demand cell reports required drivers, currently available eligible drivers, shortage, scarcity score and a `shortage`/`tight`/`limited`/`covered` level. Red cells need more drivers; amber cells have no or one spare driver; green cells have broader coverage. The same responses include each driver's automatic approximate hours and useful capacity, calculated through `RosterInputService`, so the values shown before generation are the exact soft targets later passed to the roster solver. Saving availability refreshes both the estimate and heatmap. The heatmap uses the same business-day overnight convention as roster generation, removes availability covered by approved sick leave, and never exposes other employees' names or profiles. In-store/manager personal schedules do not receive the driver heatmap or driver-hours estimate, and inside demand never influences either.

Eligible drivers' personal availability responses also expose `DriversWithoutAvailability`: an aggregate count of active roster-eligible drivers with no saved availability slots in the selected business week, including the signed-in driver if their week is empty. A single saved day counts as entered; inactive/inside employees and stale or missing driver role/profile matches are excluded. The count is independent of demand, vehicle capacity and sick-leave filtering, is refreshed after saves and through existing availability/employee change events, and never reveals another driver's identity. There is no separate submission record, so a saved all-days-off week also counts as empty. The UI shows this count between the approximate hours and availability editor, and explains that estimates may change as other drivers enter availability. Non-driver personal schedules do not receive the count.

`frontend/nginx.conf` proxies `/api/` and `/hubs/` to the backend and serves the SPA for all other paths. Keep frontend API calls same-origin and use `credentials: 'include'` for Identity cookies.

The authenticated `/hubs/application-events` SignalR hub delivers lightweight invalidation events after committed availability, demand, roster, sick-leave, holiday, employee and roster-setting changes. Connections join server-controlled management, role and employee groups; clients cannot choose another employee's group. Event payloads contain only the event type, affected entity ID, week/scenario when relevant and occurrence time. React keeps HTTP endpoints as the authoritative data source and refetches only affected domains. Inside and driver demand/roster versions are tracked separately. On reconnect or browser focus the client invalidates every domain to recover events missed while disconnected. If a demand or roster form has unsaved edits, incoming invalidations show a warning instead of silently replacing those edits. The existing admin-only `/hubs/roster-generation` stream remains separate because its detailed solver logs can contain employee scheduling information; its HTTP replay remains the recovery mechanism for generation progress.

## Demand and labour

Demand plans are weekly and keyed by Monday plus an independent scenario (`outside` for drivers or `inside` for in-store staff). Admins may select a week and scenario, import or edit its workload, staffing demand, productivity and sales targets; managers have read-only access. Changes to one scenario never modify the other. Outside demand uses delivery counts from each weekday's imported pair, while inside demand uses pizza counts and inside productivity. Roster generation loads the exact selected week's outside plan rather than reusing the latest plan. Demand shows workload, required hours, sales targets, and planned labour calculated from demand rather than roster shifts.

- `DeliveriesPerDriverHour` defaults to `2.7` and is editable from `0.01` to `1000`.
- Calculated outside demand rounds deliveries/productivity **up** to whole drivers; calculated inside demand rounds pizzas/productivity **up** to whole inside employees. Both scenarios are persisted independently by week and expose only their own workload/staffing fields in the selected view.
- Changing productivity or explicitly recalculating replaces staff-demand overrides; an ordinary save can retain explicit manual staff counts. Older request payloads preserve omitted workload/settings fields.
- Outside-demand planned labour uses a pay rate weighted by each eligible driver's automatically calculated approximate hours for the selected saved week: `sum(approximate hours × hourly rate) / sum(approximate hours)`. The API returns the driver-level hours, useful capacity, pay and approximate base cost so management can audit the mix. Unsaved demand edits continue to use the last saved mix and are labelled stale until saved; saving recalculates it. If no useful availability overlaps positive demand, the estimate is unavailable rather than falling back to an unrelated equal average. Inside demand remains independent and its planning estimate uses the average current pay rate of eligible inside staff; once the inside roster is saved, the saved-roster labour view uses the employees actually assigned and their current rates. Sunday premium is applied per calendar hour, including Saturday business-day hours after midnight and excluding Sunday business-day hours after midnight. Demand labour remains an estimate because it does not assign named employees; saved-roster labour is authoritative.
- The demand interface includes weekly, daily and per-hour labour analysis. It compares entered whole-driver demand with the fractional deliveries/productivity ideal and its rounded whole-driver requirement, and reports delivery capacity, utilisation, staffing variance, ideal versus entered-demand labour, cost variance and cost per delivery. “Actual demand labour” in this view means the current saved demand values, not a saved roster; saved-roster labour remains the authoritative named-employee cost.
- Saved-roster labour uses actual persisted shifts from the selected roster and each employee's current hourly pay rate. Drivers and in-store employees receive a 25% premium for Sunday calendar hours; managers remain on their normal rate. Overnight shifts split at calendar midnight for Sunday pay, while totals remain assigned to the shift's business day for comparison with sales targets.
- `GET /api/admin/roster/labour?weekStart=YYYY-MM-DD&rosterKind=drivers|inside` returns daily and weekly hours, cost and percentage of target sales for exactly one roster/scenario: driver rosters use outside-demand sales and inside rosters use inside-demand sales. Admins and managers may read it.
- Labour totals refresh after roster edits and are recalculated from current employee rates whenever loaded, including for older rosters. The two labour views are independent: a missing inside roster does not make driver labour incomplete and vice versa. A missing selected roster or zero/missing selected-scenario sales targets shows no percentage. The Sunday multiplier retains full decimal precision until labour costs are rounded to cents.

## Roster generation invariants

Roster generation uses OR-Tools CP-SAT and is coordinated by the roster services/timer. Important rules include:

- driver rosters are generated automatically; eligibility requires an active driver employee without an inside/manager role or profile;
- inside rosters are created and edited manually from the saved-roster view; eligibility requires an active `Manager` or `InStore` Identity role with its matching profile, managers are listed first, and every staffed inside hour requires a manager;
- generated shifts are continuous and 3–10 hours, inside availability;
- driver generation attempts exact hourly demand first; when exact coverage is proven impossible but at least one legal shift can be assigned, it saves the best under-covered roster found by the shortage-minimizing fallback and never schedules more drivers than hourly demand;
- at least one `Car` or `Moped` driver must cover each staffed hour; multiple `EBike` drivers cannot substitute for that support;
- shared company vehicles are a hard hourly limit for driver generation, under-coverage fallback, preference optimization and manual edits. Saved generator settings hold independent `CompanyCars`, `CompanyMopeds` and `CompanyEBikes` counts (whole numbers `0`–`1000`, default `0`). Zero blocks drivers needing that company vehicle, not drivers with their own. Capacity uses actual timestamps, allows a vehicle handover at a shift's finish, and includes overlapping driver shifts from adjacent saved weeks, even for employees no longer eligible. Inside rosters never consume company vehicles;
- minimum/preferred rest, latest start, fairness, and rolling history are enforced or scored according to saved settings;
- generation and saving are protected against concurrent jobs and stale input;
- failed or cancelled generation must not replace the saved roster.

Before solving, `FairDriverHoursCalculator` calculates a non-persisted estimate for the selected week and roster kind. For each positive-demand hour it scores useful availability as `demand / usable eligible employees` and raises each employee's total score to the configurable fairness alpha (default `0.7`). Driver estimates use a fractional hourly allocation: maximize covered hours within availability, hourly demand, company fleet capacity, e-bike support, 10 useful hours per business day and each employee's `MaximumWeeklyHours` (default `45`), then progressively distribute hours by scarcity-weighted max-min fairness. Shared vehicle bottlenecks therefore redistribute hours across drivers rather than granting each driver the same unavailable vehicle time. Adjacent-week vehicle reservations count here too. Inside estimates retain their independent proportional allocation with daily/weekly caps and do not use fleet settings. Zero-demand availability adds neither score nor capacity, zero weekly maximum gives zero estimated hours, and unavailable demand remains unallocated. Estimates are fractional previews, not promises of legal continuous shifts: minimum shift duration and rest remain the roster solver's responsibility. The optimizer uses approximate hours only as a soft objective after coverage: exact driver coverage is attempted first, and a proven-infeasible week falls back to minimizing uncovered staff-hours. Hourly overstaffing, availability, fleet capacity, supervision, shift length and rest remain hard constraints. `ApproximateHoursWeight` controls the fairness objective; its deployed database column retains its legacy name to avoid a data migration.

Approximate hours are recalculated whenever demand, availability, eligibility, sickness, maximum hours, vehicle ownership, fleet settings or alpha changes. They are shown in the generation summary to managers/administrators and in the driver's personal roster view, and feed outside-demand labour calculations. The driver heatmap also counts vehicle-usable hourly capacity and excludes unsupported e-bike availability, without revealing profile details. Ownership, fleet counts and adjacent-week vehicle reservations are included in driver input fingerprints so saved rosters are revalidated after these inputs change. New roster snapshots retain the exact fractional estimate and approximate-hours percentages for audit/history, but it is not stored on the employee profile. Historical fairness reads that exact snapshot estimate and never falls back to a profile target.

`RosterPlan.RosterKind` and its `(WeekStart, RosterKind)` unique key isolate driver and inside rosters for the same week. Read, history, summary, manual edit and labour endpoints accept both kinds. A missing inside roster is returned as an unsaved draft populated with eligible managers/in-store employees and current inside-demand coverage; the first valid administrator save creates it. Generation, cancellation, job logs and WebSocket progress remain driver-only, admin-only operations with one server worker and one database lock. Driver and inside history, input fingerprints and adjacent-week rest checks use only their own roster kind.

Fairness uses the previous four saved weeks for both scheduled hours versus the saved automatic estimate and average shift duration. Historical duration is total scheduled hours divided by actual shift count, pooled across valid saved shifts (not an average of weekly averages). Missing or inconsistent legacy shift details do not contribute to duration history. The independent `HistoryShiftLengthWeight` preference defaults to `100`; administrators can set it from `0` (disabled) to `1000`. It prefers a duration of `clamp(7 + group historical average - employee historical average, 6, 8)` hours for employees with valid history, using a squared duration penalty. This gives drivers with shorter past shifts a stronger preference for longer shifts, while exact demand, availability, supervision and rest remain mandatory and automatic-hours fairness remains separately weighted. The solver precomputes eight costs per employee, adding no search variables for this preference.

Manual roster edits apply to both kinds and may override generator-style constraints: administrators can save shifts shorter than 3 hours, longer than 10 hours (up to the storage-safe 24-hour maximum), after the generated latest-start limit, and outside configured shop opening/closing hours. The editor shows these warnings before applying a shift, and the server persists the warnings in the roster audit snapshot. Closed-hour extensions may exceed the employee's entered availability because availability entry is intentionally bounded to shop hours; any unavailable portion during open hours remains a hard error. Employee eligibility, one shift per business day and minimum rest remain hard server-side constraints; drivers retain e-bike support and inside employees retain manager supervision whenever anyone is staffed. Demand shortages/excess remain saveable warnings, including an incomplete inside draft. Automatic driver generation is unchanged: it still creates only 3–10-hour shifts inside availability and its latest-start limit.

Hourly demand-count mismatches are saved as warnings and highlighted in the saved roster. Generated driver rosters use exact coverage when possible; if exact coverage is proven impossible, a non-empty shortage-minimizing fallback may be saved with status `partial`, explicit hourly shortage diagnostics and no overstaffed hours. A fallback found after an unproven timeout is not saved unless its optimum proves that a shortage is unavoidable. New snapshots store separate demand and driver-availability fingerprints. Every saved-roster read compares those fingerprints with current inputs and, when either changed, recalculates current hourly coverage and reports demand, availability, eligibility, supervision, or rest mismatches without altering the saved shifts. The saved shift tables lead the page, and the historical fairness details are collapsed by default.

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
