# dotnet-pbac-boilerplate

.NET 10 Minimal API boilerplate following the **Vertical Slice Architecture**
detailed in [`prd.md`](./prd.md) (v2.1).

## Status

| Milestone | Scope | State |
| --- | --- | --- |
| **M1** | Foundation & Bootstrapping (skeleton, IEndpoint, ProblemDetails, ApiResponse) | ✅ |
| **M2** | CQRS & Data Layer (MediatR, EF Core 10, Dapper, Postgres) | ✅ |
| M3 | Resilience & Security (Redis cache, JWT, permissions) | ⏳ |
| M4 | Pure Integration Testing (Testcontainers) | ⏳ |
| M5 | Observability & DevOps (OpenTelemetry, Serilog, Dockerfile) | ⏳ |
| M6 | (Stretch) AOT profile | ⏳ |

## Prerequisites

* [.NET SDK 10.0.300](https://dotnet.microsoft.com/download) (pinned in `global.json`)
* Docker Desktop (Postgres + Redis from M2 onward)

## Quickstart

```powershell
# One-time tool restore (dotnet-ef)
dotnet tool restore

# Start Postgres + Redis
docker compose up -d

# Configure connection string (Development)
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=pbac;Username=pbac;Password=pbac" --project src\Api

# Apply migrations
dotnet ef database update --project src\Api

# Run
dotnet run --project src\Api
```

Smoke test:

```powershell
# Liveness
curl http://localhost:5000/health/live

# API info
curl http://localhost:5000/api/v1

# Create a product (M2)
curl -X POST http://localhost:5000/api/v1/products `
  -H "Content-Type: application/json" `
  -d '{ "name": "Coffee", "sku": "SKU-001", "price": 25000 }'

# Read it back
curl http://localhost:5000/api/v1/products/<id>

# OpenAPI document (Development only)
curl http://localhost:5000/openapi/v1.json
```

> The default URL is printed on startup; .NET 10 typically uses port 5000
> (HTTP) and 5001 (HTTPS) unless `launchSettings.json` overrides them.

## Local Infrastructure

```powershell
docker compose up -d   # Postgres (5432), Redis (6379)
docker compose down    # tears down (volumes persist)
```

## Database Migrations

Migrations live in `src/Api/Infrastructure/Data/Migrations` and are auto-applied
in `Development` only — production runs them via the deploy pipeline (see
PRD §9.7).

```powershell
# Add a new migration (EF Core)
dotnet ef migrations add <Name> --project src\Api --output-dir Infrastructure\Data\Migrations

# Apply pending migrations to the configured connection
dotnet ef database update --project src\Api

# Generate a SQL script (for prod review)
dotnet ef migrations script --project src\Api --idempotent --output ./artifacts/migrate.sql
```

If `dotnet ef` is not on PATH, run `dotnet tool restore` first — it's pinned
in `.config/dotnet-tools.json`.

## Repository Layout

See [`prd.md` §5](./prd.md#5-peta-direktori-vertikal-directory-map) for the
canonical directory map. Quick reference:

```
src/Api/
├── Common/         # Shared kernels (Endpoints, Exceptions, Responses, Context, Behaviors)
├── Infrastructure/ # System engines (Data: AppDbContext, IDbConnectionFactory, Migrations)
├── Features/       # Vertical slices (one folder per feature)
│   ├── System/         # /health/live, /api/v1
│   └── Products/       # CreateProduct, GetProductById
└── Program.cs
```

## Adding a Feature (Slice)

Use the canonical `CreateProduct` slice (see `src/Api/Features/Products/CreateProduct/`)
as the template. Every slice must satisfy the
[Definition of Done in PRD §8](./prd.md#8-definition-of-done-dod-per-slice)
before it is merged.

## Conventions Recap

* **VSA strict** — no `Controllers/` or `Repositories/` directories.
* **EF Core** for commands, **Dapper** for queries (PRD §6 directive #3).
* **No mocks** — integration tests use Testcontainers (PRD §6 directive #4).
* **RFC 7807** for all error responses; `ApiResponse<T>` only for the success path.
* **Permission-based authorization** via `.RequirePermission("...")` (PRD §4.1, lands in M3).
* **MediatR pipeline** — `LoggingBehavior` and `ValidationBehavior` apply automatically.

## License

TBD.
