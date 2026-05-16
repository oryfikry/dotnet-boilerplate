# 🚀 .NET 10 PBAC Boilerplate

**Production-ready .NET 10 Minimal API boilerplate** with **Permission-Based Access Control**, **Vertical Slice Architecture**, and **multi-database support** out of the box.

Stop wasting weeks on infrastructure. Start building features on day one.

---

## ✨ Why This Boilerplate?

### 🎯 **Zero-Config Start, Production-Ready Finish**

- **SQLite by default** — no Docker, no setup, just `dotnet run`
- **Switch to Postgres/MySQL/SQL Server** with one config line
- **JWT auth + permissions** already wired
- **Redis caching** (L1+L2+L3 hybrid) ready to enable
- **Integration tests** with Testcontainers — no mocks, real databases

### 🏗️ **Architecture That Scales**

- **Vertical Slice Architecture** — features are self-contained, not scattered across layers
- **CQRS with MediatR** — commands use EF Core, queries use Dapper
- **RFC 7807 Problem Details** — consistent error responses
- **Pipeline behaviors** — logging, validation, cache invalidation automatic

### 🔒 **Security First**

- **Permission-based authorization** — fine-grained control beyond roles
- **JWT with refresh tokens** — secure, stateless auth
- **Password hashing** with industry-standard algorithms
- **SQL injection protection** — parameterized queries enforced

### 🧪 **Testing Without Pain**

- **No mocks** — integration tests use real Postgres via Testcontainers
- **Fast feedback** — tests run in isolated containers
- **CI-ready** — GitHub Actions workflow included

---

## 🎁 What You Get

| Feature | Status | Description |
|---------|--------|-------------|
| **Vertical Slice Architecture** | ✅ | Features organized by capability, not technical layer |
| **Multi-Database Support** | ✅ | SQLite, Postgres, SQL Server, MySQL — switch with config |
| **CQRS + MediatR** | ✅ | Commands (EF Core) + Queries (Dapper) |
| **JWT Authentication** | ✅ | Access + refresh tokens, permission-based authorization |
| **Redis Caching** | ✅ | Hybrid L1+L2+L3 cache with Polly v8 resilience |
| **Integration Tests** | ✅ | Testcontainers Postgres, no mocks |
| **OpenAPI/Swagger** | ✅ | Auto-generated docs in Development |
| **Health Checks** | ✅ | Liveness + readiness endpoints |
| **Observability** | ⏳ | OpenTelemetry, Serilog (M5) |
| **AOT Profile** | ⏳ | Native AOT support (M6) |

---

## 🚀 Quickstart (60 Seconds)

### Prerequisites

- [.NET SDK 10.0.300](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- Docker Desktop (optional — only needed for Postgres/MySQL/SQL Server/Redis)

### Run It

```powershell
# 1. Restore tools
dotnet tool restore

# 2. Set JWT signing key (≥32 bytes UTF-8)
dotnet user-secrets set "Jwt:SigningKey" "dev-only-32-byte-jwt-signing-key-do-not-use-in-prod-1234567890" --project src\Api

# 3. Run (migrations + seed data automatic in Development)
dotnet run --project src\Api --urls http://localhost:5050
```

### Test It

```powershell
# Health check
curl http://localhost:5050/health/live

# Login
$body = '{"email":"demo@local","password":"Demo123!Demo123!"}'
$login = Invoke-WebRequest -Uri http://localhost:5050/api/v1/auth/login `
  -Method POST -ContentType 'application/json' -Body $body -UseBasicParsing
$access = ($login.Content | ConvertFrom-Json).data.accessToken

# Create a product (requires products.create permission)
Invoke-WebRequest -Uri http://localhost:5050/api/v1/products -Method POST `
  -ContentType 'application/json' `
  -Headers @{Authorization="Bearer $access"} `
  -Body '{ "name": "Coffee", "sku": "SKU-001", "price": 25000 }' -UseBasicParsing

# OpenAPI docs
curl http://localhost:5050/openapi/v1.json
```

**Demo credentials** (Development only):
- Email: `demo@local`
- Password: `Demo123!Demo123!`
- Permissions: `products.create`, `products.read`

---

## 🔄 Switch Database Providers

Set `Database:Provider` in `appsettings.{Environment}.json` or user secrets:

```jsonc
{
  "Database": { "Provider": "Postgres" },  // Sqlite | Postgres | SqlServer | MySql
  "ConnectionStrings": {
    "Sqlite":    "Data Source=App_Data/pbac.db",
    "Postgres":  "Host=localhost;Port=5433;Database=pbac;Username=pbac;Password=pbac",
    "SqlServer": "Server=localhost,1433;Database=pbac;User Id=sa;Password=Pbac!Local123;TrustServerCertificate=True",
    "MySql":     "Server=localhost;Port=3306;Database=pbac;User=pbac;Password=pbac"
  }
}
```

### Spin Up Infrastructure

```powershell
# Redis only (works with any DB provider, including SQLite)
docker compose up -d redis

# Database (pick one)
docker compose --profile postgres  up -d   # Port 5433
docker compose --profile sqlserver up -d   # Port 1433
docker compose --profile mysql     up -d   # Port 3306

# Tear down
docker compose down
```

---

## 📁 Project Structure

```
src/Api/
├── Common/         # Shared kernels (Endpoints, Exceptions, Responses, Behaviors)
├── Infrastructure/ # System engines (Data, Caching, Security)
├── Features/       # Vertical slices (one folder per feature)
│   ├── System/         # Health checks, API info
│   ├── Auth/           # Login, RefreshToken
│   └── Products/       # CreateProduct, GetProductById
└── Program.cs
```

**Every feature is self-contained** — request, handler, validator, endpoint, tests all in one folder.

---

## 🎯 Adding Features

Use `src/Api/Features/Products/CreateProduct/` as your template:

1. **Request** — input DTO
2. **Handler** — MediatR `IRequestHandler<TRequest, TResponse>`
3. **Validator** — FluentValidation
4. **Endpoint** — implements `IEndpoint`, maps route
5. **Tests** — integration tests with Testcontainers

**Conventions:**
- **EF Core** for commands (writes)
- **Dapper** for queries (reads)
- **No mocks** — integration tests use real databases
- **RFC 7807** for errors, `ApiResponse<T>` for success
- **Permission-based auth** via `.RequirePermission("feature.action")`

---

## 🧪 Testing

```powershell
# Integration tests (Testcontainers Postgres, Docker required)
dotnet test
```

Tests live in `tests/Api.IntegrationTests/` and use the canonical `BaseIntegrationTest` fixture.

---

## 🛠️ Database Migrations

Each provider has its own migrations folder:

```
src/Api/Infrastructure/Data/Migrations/
├── 20260516074807_Initial.cs        # Postgres (AppDbContext)
├── Sqlite/                          # SqliteDbContext
├── SqlServer/                       # SqlServerDbContext
└── MySql/                           # MySqlDbContext
```

### Create Migration

```powershell
# SQLite (default)
dotnet ef migrations add MigrationName --project src\Api --context SqliteDbContext --output-dir Infrastructure\Data\Migrations\Sqlite

# Postgres
dotnet ef migrations add MigrationName --project src\Api --context AppDbContext --output-dir Infrastructure\Data\Migrations

# SQL Server
dotnet ef migrations add MigrationName --project src\Api --context SqlServerDbContext --output-dir Infrastructure\Data\Migrations\SqlServer

# MySQL
dotnet ef migrations add MigrationName --project src\Api --context MySqlDbContext --output-dir Infrastructure\Data\Migrations\MySql
```

### Apply Migration

```powershell
dotnet ef database update --project src\Api --context SqliteDbContext
```

Migrations run automatically in Development on startup.

---

## 📚 Documentation

- **Architecture Decisions** — `docs/adr/`
  - [ADR-0001: Multi-Provider Database](./docs/adr/0001-multi-provider-database.md)
  - [ADR-0002: Hybrid Caching Strategy](./docs/adr/0002-hybrid-caching-strategy.md)
  - [ADR-0003: Permission-Based Authorization](./docs/adr/0003-permission-based-authorization.md)
- **API Docs** — `/openapi/v1.json` (Development only)

---

## 🎯 Key Conventions

- **Vertical Slice Architecture** — no `Controllers/` or `Repositories/` folders
- **CQRS** — EF Core for writes, Dapper for reads
- **Multi-provider SQL** — avoid provider-specific syntax (no `RETURNING`, `TOP`, `LIMIT n,m`)
- **No mocks** — integration tests use Testcontainers
- **RFC 7807** — all errors return Problem Details
- **Permission-based auth** — `.RequirePermission("feature.action")`
- **MediatR pipeline** — logging, validation, cache invalidation automatic

---

## 🚢 Production Checklist

Before deploying:

1. **Change JWT signing key** — use a cryptographically secure key (≥256 bits)
2. **Disable dev seeder** — remove `DevSeeder` registration in Production
3. **Configure database** — set production connection string
4. **Enable Redis** — configure `CacheSettings:RedisConnectionString`
5. **Set up observability** — configure OpenTelemetry + Serilog (M5)
6. **Review security** — audit permissions, validate input, check CORS

---

## 🤝 Contributing

This is a boilerplate, not a framework. Fork it, customize it, make it yours.

If you find bugs or have suggestions, open an issue or PR.

---

## 📄 License

TBD.

---

## 🎉 Get Started Now

```powershell
git clone https://github.com/yourusername/dotnet-pbac-boilerplate.git
cd dotnet-pbac-boilerplate
dotnet tool restore
dotnet user-secrets set "Jwt:SigningKey" "dev-only-32-byte-jwt-signing-key-do-not-use-in-prod-1234567890" --project src\Api
dotnet run --project src\Api --urls http://localhost:5050
```

**Stop building infrastructure. Start building features.**
