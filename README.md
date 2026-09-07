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
```
