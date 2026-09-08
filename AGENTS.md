# Roaster Generator — Codex project instructions

## Project shape

- Backend: ASP.NET Core on .NET 10 in `Roaster Generator/`.
- Database: PostgreSQL through EF Core/Npgsql. Migrations live in `Roaster Generator/Data/Migrations/` and are applied at API startup.
- Authentication: ASP.NET Core Identity with an application cookie. The API and React app are same-origin in Docker.
- Frontend: React/Vite in `frontend/`; production serves its build through Nginx and proxies `/api/` and `/hubs/` to the backend.
- Deployment: GitHub Actions builds and publishes backend/frontend images to GHCR, then the VPS runs `compose.prod.yaml` behind Traefik.

Read `docs/PROJECT_CONTEXT.md` before making substantial changes. It contains the current domain model, authorization rules, API boundaries, deployment details, and scheduler invariants.

## Change rules

- Preserve unrelated existing worktree changes. Do not use destructive commands such as `git reset --hard`, `git checkout --`, or `docker compose down -v` unless explicitly requested.
- Use `apply_patch` for source and documentation edits.
- Keep controllers thin: request validation belongs in `Roaster Generator/Validation/` using FluentValidation; authorization belongs in policies/attributes and server-side checks, never only in React.
- Do not put passwords, connection strings, GitHub secrets, cookie contents, password hashes, or other secrets in source, logs, commits, or responses.
- Do not automatically reset existing passwords during ordinary startup. Password reset flags are deliberately one-time recovery switches and must be disabled after use.
- Add or update EF migrations when the persisted model changes. Do not edit an already-applied migration to change production schema history.
- For role-specific employee data, use the appropriate profile entity rather than adding nullable role fields to `Employee`.
- Keep the driver workspace free of driver profile details. Driver profile data may be shown in admin management views only.

## Authentication and authorization

- Admin credentials come from `Auth__AdminUsername` and `Auth__AdminPassword`.
- Employee usernames are their employee numbers. Newly created employee accounts start with `12345`; existing passwords are preserved.
- Roles are `Admin`, `Driver`, `InStore`, and `Manager`. `Manager` has management-console access; `Admin` is the system administrator role.
- An employee must be active to log in or use employee endpoints. Always enforce ownership and active status on the API even if the UI hides another employee.
- The cookie is named `roaster-generator-auth`; data-protection keys are persisted to the Docker `data_protection_keys` volume.
- When diagnosing auth, distinguish a `401` from `/api/auth/login` (credentials/user state) from a `401` from `/api/auth/me` or another endpoint (cookie/session/proxy or authorization state).

## Verification

Run the checks that match the change:

```bash
dotnet build "Roaster Generator/Roaster Generator.csproj" -c Release --no-restore
dotnet test "Roaster Generator.Tests/Roaster Generator.Tests.csproj" -c Release --no-restore
docker compose -f compose.yaml build
docker compose -f compose.yaml config
```

The host may not have Node/npm available; the frontend build is executed by the frontend Dockerfile. For an end-to-end local stack use `docker compose -f compose.yaml up -d --build` and test through `http://localhost:3000/`.

Do not commit or push unless the user explicitly asks for it.
