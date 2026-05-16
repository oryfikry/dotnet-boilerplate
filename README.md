# dotnet-pbac-boilerplate

.NET 10 Minimal API boilerplate following the **Vertical Slice Architecture**
detailed in [`prd.md`](./prd.md) (v2.2).

## Status

| Milestone | Scope | State |
| --- | --- | --- |
| **M1** | Foundation & Bootstrapping (skeleton, IEndpoint, ProblemDetails, ApiResponse) | ✅ |
| **M2** | CQRS & Data Layer (MediatR, EF Core 10, Dapper, multi-provider DB) | ✅ |
| **M3** | Resilience & Security (Redis cache, JWT, permissions) | ✅ |
| **M4** | Pure Integration Testing (Testcontainers Postgres) | ✅ |
| M5 | Observability & DevOps (OpenTelemetry, Serilog, Dockerfile) | ⏳ |
| M6 | (Stretch) AOT profile | ⏳ |

## Prerequisites

* [.NET SDK 10.0.300](https://dotnet.microsoft.com/download) (pinned in `global.json`)
* Docker Desktop — only required when switching to Postgres / SQL Server / MySQL,
  or when running Redis for the M3 cache. SQLite (default) needs nothing.

## Quickstart (zero infra)

The default provider is **SQLite** (ADR-0001). The DB file lives at
`src/Api/App_Data/pbac.db` and is created automatically on first run.

```powershell
# One-time tool restore (dotnet-ef)
dotnet tool restore

# Set the JWT signing key (Development user secrets, ≥32 bytes UTF-8)
dotnet user-secrets set "Jwt:SigningKey" "dev-only-32-byte-jwt-signing-key-do-not-use-in-prod-1234567890" --project src\Api

# Run — migrations + dev seed run automatically in Development
dotnet run --project src\Api --urls http://localhost:5050
```

Smoke test:

```powershell
# Liveness
curl http://localhost:5050/health/live

# Login
$body = '{"email":"demo@local","password":"Demo123!Demo123!"}'
$login = Invoke-WebRequest -Uri http://localhost:5050/api/v1/auth/login `
  -Method POST -ContentType 'application/json' -Body $body -UseBasicParsing
$access = ($login.Content | ConvertFrom-Json).data.accessToken

# Create a product
Invoke-WebRequest -Uri http://localhost:5050/api/v1/products -Method POST `
  -ContentType 'application/json' `
  -Headers @{Authorization="Bearer $access"} `
  -Body '{ "name": "Coffee", "sku": "SKU-001", "price": 25000 }' -UseBasicParsing

# OpenAPI document (Development only)
curl http://localhost:5050/openapi/v1.json
```

Demo credentials (Development only, from `DevSeeder`):

* Email: `demo@local`
* Password: `Demo123!Demo123!`
* Permissions: `products.create`, `products.read`

## Switching the database provider

Set `Database:Provider` in `appsettings.{Environment}.json` (or via user secrets)
to one of `Sqlite`, `Postgres`, `SqlServer`, `MySql`. Connection strings live
under `ConnectionStrings:{Provider}`. See
[`docs/adr/0001-multi-provider-database.md`](./docs/adr/0001-multi-provider-database.md).

```jsonc
{
  "Database": { "Provider": "Postgres" },
  "ConnectionStrings": {
    "Sqlite":    "Data Source=App_Data/pbac.db",
    "Postgres":  "Host=localhost;Port=5433;Database=pbac;Username=pbac;Password=pbac",
    "SqlServer": "Server=localhost,1433;Database=pbac;User Id=sa;Password=Pbac!Local123;TrustServerCertificate=True",
    "MySql":     "Server=localhost;Port=3306;Database=pbac;User=pbac;Password=pbac"
  }
}
```

### Local infrastructure

`docker-compose.yml` uses profiles so you only spin up what you need:

```powershell
# Redis only (M3 cache; works alongside any provider, including SQLite)
docker compose up -d redis

# Switch DB provider — pick one
docker compose --profile postgres  up -d   # Postgres on host port 5433
docker compose --profile sqlserver up -d   # SQL Server on host port 1433
docker compose --profile mysql     up -d   # MySQL    on host port 3306

# Tear down (volumes persist)
docker compose down
```

## Database Migrations

Per ADR-0001, each non-default provider has its own migrations folder bound to
its own design-time `DbContext` subclass. Postgres keeps the historical flat
layout for backward compatibility.

```
src/Api/Infrastructure/Data/Migrations/
├── 20260516074807_Initial.cs        # Postgres (AppDbContext)
├── AppDbContextModelSnapshot.cs
├── Sqlite/                          # SqliteDbContext
├── SqlServer/                       # SqlServerDbContext
└── MySql/                           # MySqlDbContext
```

In Development the seeder calls `db.Database.MigrateAsync()` automatically
on startup. To manage migrations manually:

```powershell
# SQLite
dotnet ef migrations add <Name> --project src\Api --context SqliteDbContext --output-dir Infrastructure\Data\Migrations\Sqlite
dotnet ef database update         --project src\Api --context SqliteDbContext

# PostgreSQL (default context)
dotnet ef migrations add <Name> --project src\Api --output-dir Infrastructure\Data\Migrations
dotnet ef database update         --project src\Api

# SQL Server
dotnet ef migrations add <Name> --project src\Api --context SqlServerDbContext --output-dir Infrastructure\Data\Migrations\SqlServer
dotnet ef database update         --project src\Api --context SqlServerDbContext

# MySQL (Oracle provider — Pomelo has no EF10 release as of 2026-05)
dotnet ef migrations add <Name> --project src\Api --context MySqlDbContext --output-dir Infrastructure\Data\Migrations\MySql
dotnet ef database update         --project src\Api --context MySqlDbContext
```

If `dotnet ef` is not on PATH, run `dotnet tool restore` first — it's pinned
to `10.0.8` in `.config/dotnet-tools.json`.

## Repository Layout

See [`prd.md` §5](./prd.md#5-peta-direktori-vertikal-directory-map) for the
canonical directory map. Quick reference:

```
src/Api/
├── Common/         # Shared kernels (Endpoints, Exceptions, Responses, Context, Behaviors)
├── Infrastructure/ # System engines
│   ├── Data/           # AppDbContext, providers, IDbConnectionFactory, Migrations
│   ├── Caching/        # ICacheService, HybridCacheService (L1+L2+L3, Polly v8)
│   └── Security/       # JwtTokenService, PasswordHasher, RequirePermissionFilter
├── Features/       # Vertical slices (one folder per feature)
│   ├── System/         # /health/live, /api/v1
│   ├── Auth/           # Login, RefreshToken
│   └── Products/       # CreateProduct, GetProductById
└── Program.cs
```

## Adding a Feature (Slice)

Use the canonical `CreateProduct` slice (see `src/Api/Features/Products/CreateProduct/`)
as the template. Every slice must satisfy the
[Definition of Done in PRD §8](./prd.md#8-definition-of-done-dod-per-slice)
before it is merged.

When writing **Dapper** queries, keep SQL provider-portable:

* Parameterize boolean predicates (`is_deleted = @IsDeleted`, not `= FALSE`).
* Avoid PG-only constructs (`RETURNING`, `::cast`), MS-only (`TOP`, `OUTPUT`),
  and MySQL-only (`LIMIT n,m` form).
* Stick to ANSI SQL where possible; provider-specific helpers belong in EF.

When writing **EF entity configurations**, use `HasPrecision(p, s)` for
decimals — `HasColumnType("numeric(18,4)")` is PG-only and breaks SQLite.

## Conventions Recap

* **VSA strict** — no `Controllers/` or `Repositories/` directories.
* **EF Core** for commands, **Dapper** for queries (PRD §6 directive #3).
* **Multi-provider DB** — SQLite default, Postgres/SqlServer/MySql opt-in (ADR-0001).
* **No mocks** — integration tests use Testcontainers (PRD §6 directive #4).
* **RFC 7807** for all error responses; `ApiResponse<T>` only for the success path.
* **Permission-based authorization** via `.RequirePermission("...")` (PRD §4.1).
* **MediatR pipeline** — `LoggingBehavior`, `ValidationBehavior`,
  `CacheInvalidationBehavior` apply automatically.
* **SQL portability** — CI enforces no literal `TRUE`/`FALSE` in Dapper queries.
  Use parameterized predicates (`@IsDeleted`) for cross-provider compatibility.

## Tests

```powershell
# Integration tests run a real Postgres in a Testcontainer (Docker required).
# Redis is intentionally NOT containerized — HybridCacheService runs L1+L3.
dotnet test
```

The suite lives at `tests/Api.IntegrationTests/` and is wired through the
canonical `BaseIntegrationTest`. CI is configured in
`.github/workflows/ci.yml`.

## License

TBD.
