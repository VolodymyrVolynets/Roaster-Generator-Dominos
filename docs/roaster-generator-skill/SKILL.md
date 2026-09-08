---
name: roaster-generator-project
description: Work on this Roaster Generator repository with awareness of its ASP.NET Core/PostgreSQL/React architecture, employee roles, cookie authentication, holiday workflow, Docker deployment, and roster constraints.
---

# Roaster Generator project skill

Use this skill for implementation, debugging, review, or deployment-related work in this repository.

First read the repository `AGENTS.md` and `docs/PROJECT_CONTEXT.md`. Treat those files as the maintained source of project-specific conventions; inspect the actual code when the request concerns behavior that may have changed.

## Important routing decisions

- Auth failures: test `/api/auth/login` and then `/api/auth/me` separately. A successful login followed by a failed `/me` request indicates cookie, data-protection, proxy, or active-user state rather than a bad role.
- Authorization: keep role checks on the API. The frontend role-based workspaces are presentation and navigation, not a security boundary.
- Employee fields: shared identity fields belong on `Employee`; driver, in-store, and manager fields belong on their profile entities.
- Validation: add request rules to the matching FluentValidation class and keep controller/service validation orchestration small.
- Schema: add a new EF migration for database changes and preserve the existing migration history.
- Deployment: production uses prebuilt GHCR images and `compose.prod.yaml`; a source change is not live until the image is rebuilt/published and the VPS pulls it.

## Current UX boundary

Drivers see availability and holiday workflows, but not driver type, target hours, or can-work-alone details. Admin management views may show and edit those fields.

## Safe operational behavior

- Preserve PostgreSQL named volumes and data-protection keys.
- Never expose secrets or password hashes while diagnosing login.
- The password reset environment switches are one-time recovery tools only; turn them off immediately after the required reset.

For the complete model, endpoint, deployment, and verification reference, read `docs/PROJECT_CONTEXT.md`.
