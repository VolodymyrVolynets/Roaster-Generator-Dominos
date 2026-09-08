# Roaster Generator

ASP.NET Core API, PostgreSQL, and a React/Vite frontend targeting .NET 10.

## Run locally

```bash
dotnet run --project "Roaster Generator/Roaster Generator.csproj"
```

The API is available at `http://localhost:5029/` and `http://localhost:5029/api/hello`.

## Run the full stack locally with Docker

```bash
docker compose up --build
```

Open `http://localhost:3000/`. The backend is available at `http://localhost:8080/`; the React frontend calls `/api/hello`, and the API reads the PostgreSQL server time.

To force fresh base images and rebuild both application images without using the Docker build cache:

```bash
docker compose \
  -f compose.yaml \
  -f compose.rebuild.yaml \
  up -d --build --force-recreate
```

## Production configuration

Production uses `compose.prod.yaml` with Traefik routing:

- `https://dominos-roaster.online/` routes to React.
- `https://dominos-roaster.online/api/*` routes to ASP.NET Core.
- PostgreSQL is private to the Docker network and persists in a named volume.

Create `/opt/roaster-generator/.env` on the VPS before the first production deployment:

```dotenv
POSTGRES_PASSWORD=replace-with-a-long-random-password
ADMIN_USERNAME=admin
ADMIN_PASSWORD=replace-with-a-long-random-password
```

Employee accounts use their `EmployeeNumber` as the username and are created with
the initial password `12345`. Change this flow before production use if employees
need to choose their own passwords.

## Roster generation

Administrators can use **Generate roster** to generate one of the next three weeks,
and **Saved rosters** to review the result or browse previously saved weeks. The
latest imported Monday–Sunday demand template is reused for the selected week.
Admins can edit a saved roster by adding, removing, reassigning, or retiming shifts;
the complete edited set is revalidated before it replaces the saved week, and the
updated hours, percentages, coverage, rest, and warnings are displayed immediately.
Enter demand for every open hour; explicit zero demand is respected. Hours outside
the configured shop opening times are excluded and reported in the generation log.
Early-morning hours belong to the previous business day and are marked `+1 day`.

The scheduler uses [OR-Tools CP-SAT](https://developers.google.com/optimization/cp/cp_solver).
It enumerates legal shifts and requires exact driver counts at every hour, with no
understaffing or overstaffing. Each employee can have one continuous shift per
business day, lasting 3–10 hours and contained in their entered availability.
The default latest shift start is 20:00. Administrators can change it in
generation preferences between 06:00 and 22:00 inclusive; later starts,
including after midnight in the same business day, are forbidden. Shifts can
still finish after midnight.
At least one employee marked **Can work alone** must cover each staffed hour.
Rest is checked against other generated shifts and adjacent saved weeks.

Saved admin preferences control:

- Fairness of scheduled hours as a percentage of each employee's target, using
  a convex penalty so uneven allocations remain costly even when some staff are unavailable.
- Compensation for allocations in the previous four saved calendar weeks, plus
  a separate strong penalty for large percentage gaps. Historical target hours
  come from the saved snapshot, not today's employee settings. Missing weeks and
  old rosters without target snapshots are excluded rather than treated as zero work.
- Longer shifts, with 6–8 hours preferred and 8 hours best in that range;
  shorter and 9–10 hour shifts remain available when coverage requires them.
- Short shifts, number of shifts, and rest below the preferred rest goal.
- Minimum rest (default 8 hours), preferred rest (default 12 hours), and a total
  solver budget of 1–120 seconds (default 20).
- Latest shift start (default 20:00), a hard limit configurable from 06:00 to
  22:00. Overnight finishes remain valid when the start is within the limit.

Targets are proportional goals, not hard weekly caps; percentages may exceed 100%
when demand requires it. Zero-target employees can act as reserves, with a penalty
when fairness is enabled, and their target percentage is shown as N/A. Employees
without availability remain visible with zero assigned hours. Preference weights
range from 0 to 1000; zero disables the preference. Hard rules are never relaxed
to improve a preference score.

One background job runs at a time per server, using one solver CPU worker. A
PostgreSQL advisory lock also prevents simultaneous roster saves/generation across
API replicas. The search obtains a complete roster before improving preferences;
bounded searches distinguish a valid result from proven optimality, an impossible
schedule, and an inconclusive timeout. Candidate and employee limits reject
oversized requests explicitly instead of silently discarding options.

SignalR streams stages, progress, warnings, and specific shortage explanations
(for example, `Sunday 13:00: need 1 more driver`). Failed diagnostic assignments
are never saved as rosters. Closing a tab does not cancel a job; the admin can
cancel explicitly. Logs can be replayed after reconnecting and are retained in
memory for the latest job of up to 32 weeks (up to 1024 events per job); process
restarts clear these logs. Saved rosters remain in PostgreSQL.

Before committing, the server independently verifies all hard constraints and
checks that the demand, employees, availability, settings, and adjacent shifts
have not changed. Saving replaces that week's shifts and audit snapshot in one
transaction. Failed or cancelled generation preserves the previous roster.
The snapshot preserves demand, employee names/targets, applied settings and
metrics for later review. Older rosters without a snapshot show coverage as
unknown. The saved view includes hourly coverage, percentage allocation,
observed minimum rest, average hours per shift (overall and per employee), warnings,
and CSV export. Averages use total shift duration divided by the number of shifts,
rounded to two decimal places, and are also available for older saved rosters.

The fairness view compares this week's percentage with the preceding four weeks
and the combined period, and shows the history-based equalisation goal. It flags
current gaps above 30 percentage points and identifies employees whose availability
cannot reach an equal share. Fairness remains a preference alongside exact coverage:
availability, mandatory rest and discrete shift lengths can prevent equal percentages.
History uses **scheduled hours**; the application has no attendance or timesheet data
to verify hours actually worked. Regenerating a week replaces that week only and
never includes its old roster in its own four-week fairness history.

The new migration is applied automatically at API startup. Rebuild/restart both
API and frontend to use the new generator. No production deployment is required
to run the checks below:

```bash
dotnet test "Roaster Generator.Tests/Roaster Generator.Tests.csproj" -c Release
cd frontend
npm test
npm run build
```

Admin endpoints: `GET/PUT /api/admin/roster/settings`,
`POST /api/admin/roster/generate`, `POST /api/admin/roster/cancel`,
`GET /api/admin/roster/jobs?weekOffset=1`, `GET /api/admin/roster?weekOffset=1`,
`GET /api/admin/roster?weekStart=YYYY-MM-DD`, and `GET /api/admin/roster/history`.
The SignalR endpoint is `/hubs/roster-generation`, event `rosterGenerationProgress`.
Generation/cancellation bodies include `weekOffset`; cancellation may also include
`jobId` to avoid cancelling a different job. All these endpoints require Admin.
