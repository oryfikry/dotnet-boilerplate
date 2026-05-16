# Project TODO — `dotnet-pbac-boilerplate`

Status snapshot terhadap PRD v2.2 (lihat [`prd.md`](./prd.md), ADR-0001).
Diperbarui: 2026-05-16.

Repository: <https://github.com/oryfikry/dotnet-boilerplate>

---

## Ringkasan Milestone

| M | Nama | Status | Commit |
|---|---|---|---|
| **M1** | Foundation & Bootstrapping | ✅ Done | `86537a2` |
| **M2** | CQRS & Data Layer | ✅ Done | `7cdf659` |
| **M2.5** | Multi-provider DB (ADR-0001) | ✅ Done | — |
| **M3** | Resilience & Security | ✅ Done | `03e153c` |
| **M4** | Pure Integration Testing | ✅ Done | — |
| **M5** | Observability & DevOps | ⏳ Pending | — |
| **M6** | (Stretch) AOT Profile | ⏳ Pending | — |

---

## ✅ M1 — Foundation & Bootstrapping (DONE)

### Deliverables
- [x] Solution `DotnetPbacBoilerplate.slnx` + project `src/Api/Api.csproj`
- [x] `global.json` pin SDK `10.0.300`, `rollForward=latestFeature`
- [x] `Directory.Build.props` (TFM `net10.0`, LangVersion `latest`, `Nullable+ImplicitUsings`, `TreatWarningsAsErrors=true`, CPM aktif)
- [x] `Directory.Packages.props` (Central Package Management, versi pinned)
- [x] `.gitignore` standar .NET
- [x] `IEndpoint` marker interface (`static abstract void MapEndpoint`)
- [x] `EndpointRegistrationExtensions.MapEndpoints()` auto-discovery via reflection (PRD §7.1)
- [x] `GlobalExceptionHandler` → RFC 7807 ProblemDetails (PRD §4.4)
- [x] `ApiResponse<T>` success-only wrapper + `ApiResponseEndpointFilter` (PRD §6 directive #6)
- [x] `IRequestContext` skeleton + `NullRequestContext` (placeholder, di-replace di M3)
- [x] `Program.cs` bootstrapper (ProblemDetails, ExceptionHandler, OpenAPI, MapEndpoints)
- [x] `SystemEndpoints` (`/health/live`, `/api/v1`)
- [x] `docker-compose.yml` (Postgres 16-alpine, Redis 7-alpine, OTel commented)
- [x] `appsettings.json` + `appsettings.Development.json`
- [x] `README.md`
- [x] `dotnet build` hijau (0 warning, 0 error)
- [x] Smoke test 3 endpoint live (`/health/live`, `/api/v1`, `/openapi/v1.json`)

### Catatan
- Suppress `CA1000` (factory pada generic) + `CA2007` (no SyncContext di ASP.NET Core) di `Directory.Build.props`.
- `GlobalExceptionHandler` pakai `LoggerMessage` source-generator (CA1848).

---

## ✅ M2 — CQRS & Data Layer (DONE)

### Deliverables
- [x] Aktifkan paket M2 di CPM: `MediatR 12.4.1`, `FluentValidation 11.11.0` (+ DI ext), `EF Core 10.0.0`, `Npgsql 10.0.0`, `Npgsql.EFCore 10.0.0`, `Dapper 2.1.66`, `EF Core Design 10.0.0` (private)
- [x] `Common/Behaviors/ValidationBehavior.cs` (FluentValidation pipeline)
- [x] `Common/Behaviors/LoggingBehavior.cs` (LoggerMessage source-gen, log start/success/failure dgn duration)
- [x] `GlobalExceptionHandler` ditingkatkan menangani `FluentValidation.ValidationException` → `HttpValidationProblemDetails 400` dgn field errors per property
- [x] `Infrastructure/Data/AppDbContext.cs` (audit auto-stamp + soft-delete on `EntityState.Deleted`, PRD §9.6)
- [x] `Infrastructure/Data/AppDbContextFactory.cs` (design-time, fallback connection string + UserSecrets aware)
- [x] `Infrastructure/Data/IDbConnectionFactory.cs` + `NpgsqlConnectionFactory` + `DatabaseOptions`
- [x] Entities: `Product`, `User`, `Permission`, `UserPermission`, `RefreshToken`, `IdempotencyKey`
- [x] Base entities: `AuditableEntity`, `SoftDeletableEntity`
- [x] `EntityConfigurations.cs` (snake_case, unique indices, composite keys, query filters `!IsDeleted`)
- [x] Slice `Features/Products/CreateProduct` (Endpoint + Command + Validator + Handler EF Core)
- [x] Slice `Features/Products/GetProductById` (Endpoint + Query + Handler Dapper raw SQL)
- [x] `Program.cs` di-wire: MediatR (Logging+Validation behaviors), FluentValidation, AppDbContext, `IDbConnectionFactory`
- [x] Migration `Initial` (`20260516074807_Initial`): products + users + permissions + user_permissions + refresh_tokens + idempotency_keys
- [x] `dotnet-ef 10.0.0` di `.config/dotnet-tools.json`
- [x] `docker-compose.yml` Postgres dipindah ke port **5433** (host punya postgres native di 5432)
- [x] User secret `ConnectionStrings:Postgres` diset
- [x] Migration applied ke db `pbac`
- [x] README.md diperbarui dengan instruksi M2
- [x] Smoke test e2e: `POST /api/v1/products` → 200 dgn `ApiResponse<Guid>`, `GET /api/v1/products/{id}` → 200 dgn `ApiResponse<ProductDto>`, validation 400 RFC 7807

### Bug ditemukan & diperbaiki
- `ApiResponseEndpointFilter` tidak meng-unwrap `Results<TR1, TR2, ...>` discriminated unions → fix: detect via type name lalu ambil `.Result` property via refleksi.
- MediatR 12.4 `RequestHandlerDelegate` tanpa CT param → `next()` bukan `next(ct)`.

---

## ✅ M2.5 — Multi-provider Database (ADR-0001) (DONE)

PRD v2.2 menerima [`docs/adr/0001-multi-provider-database.md`](./docs/adr/0001-multi-provider-database.md).
SQLite menjadi default; Postgres/SQL Server/MySQL tetap *first-class* via `Database:Provider`.

### Deliverables
- [x] Bump EF Core stack ke `10.0.8` (Microsoft.EntityFrameworkCore + Sqlite + SqlServer + Design + Relational + Microsoft.Data.Sqlite). Postgres provider tetap `10.0.0`. MySQL provider `MySql.EntityFrameworkCore 10.0.7` (Oracle, karena Pomelo belum punya release EF10).
- [x] Tambah ADO drivers: `Microsoft.Data.SqlClient 7.0.0`, `MySqlConnector 2.5.0`.
- [x] Bump `dotnet-ef` tool ke `10.0.8`.
- [x] `IDbConnectionFactory` 4 implementasi: `SqliteConnectionFactory`, `NpgsqlConnectionFactory`, `SqlServerConnectionFactory`, `MySqlConnectionFactory`. `DatabaseProvider` enum.
- [x] Per-provider design-time DbContext subclasses: `SqliteDbContext`, `SqlServerDbContext`, `MySqlDbContext`. Postgres tetap pakai base `AppDbContext` (preserve existing migration).
- [x] `AppDbContext` tidak lagi `sealed`; tambah `protected` ctor untuk subclassing dengan `DbContextOptions<TSubclass>`.
- [x] Per-provider design-time factories + helper `DesignTimeConfiguration` (resolve `ConnectionStrings:{Provider}` → `Default` → fallback).
- [x] `Infrastructure/Data/DatabaseServiceCollectionExtensions.AddAppDatabase()` — wire provider berdasarkan `Database:Provider`. `Program.cs` dipersingkat.
- [x] `EntityConfigurations`: `Price` pakai `HasPrecision(18, 4)` (provider-neutral) menggantikan `HasColumnType("numeric(18,4)")`.
- [x] `GetProductByIdHandler` Dapper SQL: `is_deleted = @IsDeleted` (parameterized) menggantikan `is_deleted = FALSE`.
- [x] `SqliteDapperTypeHandlers` — register sekali saat startup untuk SQLite, handler `Guid`/`Guid?`/`DateTime`/`DateTime?`/`decimal`/`decimal?` (TEXT round-trip via ISO-8601/invariant).
- [x] Generate `Migrations/Sqlite/Initial`, `Migrations/SqlServer/Initial`, `Migrations/MySql/Initial`. Existing `Migrations/20260516074807_Initial` untuk Postgres tetap.
- [x] `DevSeeder` ganti `EnsureCreatedAsync` → `MigrateAsync` (provider-agnostic).
- [x] `appsettings.json` + `appsettings.Development.json`: `Database:Provider=Sqlite`, `ConnectionStrings:Sqlite=Data Source=App_Data/pbac.db`, contoh provider lain di-comment.
- [x] `docker-compose.yml`: Redis tetap default. Postgres/SQL Server (mssql 2022)/MySQL 8.4 di-gate via compose `profiles` (`postgres`, `sqlserver`, `mysql`).
- [x] PRD bump ke v2.2 (§3.1 + §3.1.1 tabel provider).
- [x] README rewrite dengan SQLite quickstart + provider switching guide.
- [x] `dotnet build` hijau (0 warning, 0 error).
- [x] Smoke test e2e SQLite default PASS:
  - Anonymous POST → **401** ✓
  - Login (`demo@local` / `Demo123!Demo123!`) → **200** dgn JWT + refresh ✓
  - Authed POST `/products` → **200** ✓
  - GET `/products/{id}` (cache miss → DB → populate) → **200** ✓
  - GET kedua (cache hit) → **200** identik ✓
  - Refresh → **200** dgn token baru ✓
  - Refresh dgn token lama (revoked) → **401** ✓

### Bug ditemukan & diperbaiki
- Initial smoke test SQLite gagal di Dapper materialization: SQLite menyimpan `Guid`/`DateTime`/`decimal` sebagai TEXT, dan default Dapper mapper tidak bisa coerce TEXT ke positional record ctor `ProductDto(Guid, …, decimal, DateTime)`. Fix: register `SqlMapper.TypeHandler<T>` global untuk SQLite (lihat `SqliteDapperTypeHandlers`).
- `dotnet-ef` tool ada di `10.0.0` sementara CPM bump ke `10.0.8` → ketidaksesuaian saat `migrations add`. Fix: bump `.config/dotnet-tools.json`.

### Catatan
- Pomelo `Pomelo.EntityFrameworkCore.MySql` belum punya release EF10 per Mei 2026 — pakai Oracle `MySql.EntityFrameworkCore 10.0.7` sementara.
- Setiap perubahan skema kini = **4 migrasi**: SqliteDbContext, SqlServerDbContext, MySqlDbContext, dan `AppDbContext` (Postgres). Acceptable untuk boilerplate kecil.
- EF10 berhenti membungkus seluruh `MigrateAsync` dalam satu transaksi; per-migration tetap atomic. Catat di runbook produksi.

---

## ✅ M3 — Resilience & Security (DONE)

### Deliverables
- [x] Aktifkan paket M3 di CPM: `StackExchange.Redis 2.8.16`, `Polly 8.5.0`, `Polly.Extensions 8.5.0`, `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0`
- [x] `Common/Caching/InvalidatesCacheAttribute.cs`
- [x] `Infrastructure/Caching/ICacheService.cs` + `CacheOptions.cs` (KeyPrefix, DefaultTtl, L2Timeout=500ms, L2RetryAttempts=2, CircuitBreakerFailureRatio=0.5)
- [x] `Infrastructure/Caching/HybridCacheService.cs` (Polly v8 ResiliencePipeline: Timeout → Retry exp → CircuitBreaker; L1=`IMemoryCache`, L2=Redis dgn SET tag membership, L3=factory delegate)
- [x] `Common/Behaviors/CacheInvalidationBehavior.cs` (baca `[InvalidatesCache]` di Command, panggil `InvalidateByTagAsync` setelah handler sukses, `MarkMutated` di `IRequestContext`)
- [x] `Infrastructure/Security/JwtOptions.cs` (Issuer, Audience, AccessTokenLifetime=15m, RefreshTokenLifetime=7d)
- [x] `Infrastructure/Security/JwtTokenService.cs` (HS256, claims sub+jti+iat+permissions; refresh token: 32-byte CSPRNG → SHA-256 hash)
- [x] `Infrastructure/Security/PasswordHasher.cs` (PBKDF2-SHA256, 200_000 iterasi, 16-byte salt, 32-byte hash, format versioned `v1.{iter}.{salt}.{hash}`, `FixedTimeEquals` verify)
- [x] `Infrastructure/Security/HttpContextRequestContext.cs` (replace `NullRequestContext`; UserId dari sub claim, Permissions dari claim "permissions" repeated)
- [x] `Infrastructure/Security/RequirePermissionFilter.cs` + extension `.RequirePermission(string)` (401 anonymous, 403 missing perm, RFC 7807)
- [x] Slice `Features/Auth/Login` (POST /api/v1/auth/login, rate limit `sensitive`, AllowAnonymous, ApiResponseFilter)
- [x] Slice `Features/Auth/RefreshToken` (POST /api/v1/auth/refresh, rotasi: revoke old + ReplacedByTokenId pointing ke new GuidV7, issue new pair)
- [x] `Program.cs` di-wire M3: `AddHttpContextAccessor`, `TimeProvider.System`, `IRequestContext` → `HttpContextRequestContext`, `CacheInvalidationBehavior` di pipeline MediatR, `IMemoryCache`, `IConnectionMultiplexer` (conditional), `HybridCacheService`, `JwtOptions` dgn validate SigningKey ≥32 bytes `ValidateOnStart`, `IPasswordHasher`+`IJwtTokenService`, `AddAuthentication(JwtBearer)` dgn `TokenValidationParameters` lengkap (Issuer, Audience, Lifetime, IssuerSigningKey, ClockSkew=30s), `AddAuthorization`, `AddRateLimiter` (global default 100/min per IP, sensitive policy 10/min)
- [x] Pipeline order: `ExceptionHandler → StatusCodePages → RateLimiter → Authentication → Authorization → MapEndpoints`
- [x] `CreateProductCommand` dapat `[InvalidatesCache("products")]`
- [x] `CreateProductEndpoint` dapat `.RequirePermission("products.create")`
- [x] `GetProductByIdEndpoint` dapat `.RequirePermission("products.read")`
- [x] `GetProductByIdHandler` memakai `ICacheService` (key `products:{id}`, tag `products`, TTL 5m, bypass via `RecentlyMutatedAggregates` untuk read-your-writes)
- [x] `Infrastructure/Data/Seeders/DevSeeder.cs` (seed `products.create` + `products.read` permissions, demo user `demo@local` / `Demo123!Demo123!` dgn kedua permissions; idempotent; dipanggil hanya di Development)
- [x] User secret `Jwt:SigningKey` dan `Cache:RedisConnectionString` diset
- [x] `dotnet build` hijau
- [x] Smoke test e2e PASS:
  - Anonymous POST → **401** ✓
  - Login → **200** dgn JWT + refresh ✓
  - Authed POST → **200** ✓
  - GET pertama (cache miss → DB → populate) → **200** ✓
  - GET kedua (cache hit) → **200** identik ✓
  - Refresh → **200** dgn token baru ✓
  - Refresh dgn token lama (revoked) → **401** ✓

### Bug ditemukan & diperbaiki
- `RedisValue` → `byte[]` ambig dgn `string` di overload `JsonSerializer.Deserialize` → fix: cast eksplisit `(byte[]?)raw`.
- Konflik nama `RefreshToken` (namespace `Features.Auth.RefreshToken` vs entity `Infrastructure.Data.Entities.RefreshToken`) → fix: fully-qualify entity di Login handler.
- `NU1510` `Microsoft.Extensions.Caching.Memory` redundant (sudah di shared framework ASP.NET Core 10) → hapus dari CPM dan reference.
- Suppress `CA1711` (suffix `Permission` reserved) — domain kita memang butuh nama tersebut.

### Catatan & known limitations
- `TypedResults.Created<T>` (status 201) di-unwrap & re-wrap oleh `ApiResponseEndpointFilter` menjadi `Ok(ApiResponse<T>)` (status 200). Sesuai PRD §6 directive #6 (filter mengontrol envelope) tapi mengorbankan semantik "Created". Bisa diperbaiki nanti jika perlu.
- `IEndpointFilter.AddEndpointFilter<T>()` butuh tipe public — `ApiResponseEndpointFilter` sebenarnya internal tetapi `InternalsVisibleTo` membantu.
- Dev seeder hard-codes credentials demo. Production tidak menjalankannya.
- README belum diupdate untuk M3 — akan disatukan di update besar setelah M4/M5.

---

## ✅ M4 — Pure Integration Testing (DONE)

Cakupan PRD §11 — pure integration testing dengan Testcontainers Postgres.

### Deliverables
- [x] Aktifkan paket testing di CPM: `xunit 2.9.2`, `xunit.runner.visualstudio 3.0.1`, `Microsoft.NET.Test.Sdk 17.12.0`, `Microsoft.AspNetCore.Mvc.Testing 10.0.0`, `Testcontainers 4.1.0`, `Testcontainers.PostgreSql 4.1.0`, `Shouldly 4.2.1`, `Respawn 6.2.1`. (Pomelo MySQL tetap nonaktif — lihat M2.5.)
- [x] Project `tests/Api.IntegrationTests/Api.IntegrationTests.csproj` ditambahkan ke `.slnx`.
- [x] `tests/Directory.Build.props` — import root `Directory.Build.props` + relax warnings untuk test code.
- [x] `Infrastructure/IntegrationTestWebAppFactory.cs` — `WebApplicationFactory<Program>` yang:
  - Boot Postgres 16-alpine via Testcontainers.
  - Strip JSON config sources host, inject in-memory test config.
  - Override DI post-factum: drop SQLite/SqlServer/MySql DbContext + `IConnectionMultiplexer`, register `AppDbContext` via `UseNpgsql` ke connection-string container, dan `NpgsqlConnectionFactory`.
  - Replace `RateLimiterOptions` dengan no-op limiter (`sensitive` policy) untuk hindari 429 di test loops.
  - Redis sengaja TIDAK di-containerize — `HybridCacheService` jalan L1+L3 (path produksi saat Redis degraded).
- [x] `Infrastructure/IntegrationTestCollection.cs` — `[CollectionDefinition("Integration")]` share container antar test class.
- [x] `Infrastructure/BaseIntegrationTest.cs`:
  - `[Collection("Integration")]` + `IAsyncLifetime`.
  - Apply migrations sekali via static gate.
  - `Respawner` reset DB antar test (skip `__EFMigrationsHistory`).
  - Per-test `IServiceScope` exposes `Sender`, `Db`, `Cache`, `Hasher`, `Tokens`.
  - Helper: `SeedUserAsync(email, password, isActive, permissions[])`, `IssueAccessToken(user, permissions[])`, `CreateClient(bearerToken)`, `LoginHttpAsync(email, password)`.
- [x] Tests `Features/Authorization/PermissionFilterTests.cs` — anon→401, missing-perm→403, granted-perm→200, malformed-token→401.
- [x] Tests `Features/Products/CreateProduct/CreateProductTests.cs` — happy path persists + audit, validation 400 (invalid SKU + negative price), 401 anon, 403 missing perm.
- [x] Tests `Features/Products/GetProductById/GetProductByIdTests.cs` — happy + 404 + cache L1 hit (mutate via raw SQL → second HTTP read still cached) + read-your-writes (Sender shared scope returns fresh value) + 401/403.
- [x] Tests `Features/Auth/Login/LoginTests.cs` — happy (token pair + persisted hashed refresh), wrong password 401, unknown email 401, inactive user 401, case-insensitive email.
- [x] Tests `Features/Auth/RefreshToken/RefreshTokenTests.cs` — rotation (new pair + old revoked + ReplacedByTokenId set), reuse-revoked 401, unknown 401, empty 400.
- [x] `Program.cs` di-tambah env-aware `RateLimiting:Enabled` flag (default true) — tetap aman di Dev/Prod.
- [x] CI: `.github/workflows/ci.yml` — build + test on push/PR ke main, upload TRX results sebagai artifact.
- [x] `dotnet test` hijau end-to-end: **24/24 passing** dalam 6 detik (cold container start + tests).

### Definition of Done per slice (PRD §8) — checklist global
Setiap slice yang sudah ada (`CreateProduct`, `GetProductById`, `Login`, `RefreshToken`):
- [x] Min 2 integration tests (1 happy, 1 negative validasi/permission)
- [x] Permission ditambahkan ke seed migration (sudah dilakukan via `DevSeeder` di M3, plus `SeedUserAsync` helper di tests)
- [x] `dotnet test` hijau di CI

### Bug ditemukan & diperbaiki
- **Config override timing**: `WebApplicationFactory.ConfigureAppConfiguration` callback fires *after* top-level `Program.cs` synchronously reads config (`AddAppDatabase`, `AddRateLimiter`). Awal mencoba override `Database:Provider` via in-memory config — gagal karena `AddAppDatabase` sudah resolve SQLite sebelumnya. Fix: lakukan DI swap (`ConfigureTestServices` → `RemoveAll<DbContextOptions<...>>` + register Postgres-bound stack).
- **Rate-limit policy collision**: setelah override config gagal, mencoba `services.Configure<RateLimiterOptions>(opts => opts.AddPolicy("sensitive", ...))` — gagal `ArgumentException: There already exists a policy with the name sensitive`. Fix: drop semua `IConfigureOptions<RateLimiterOptions>` registrations, register fresh `ConfigureNamedOptions` yang produces a no-op limiter. Hasilnya empty policy table di test host.
- **Cache test design**: initial `GET_caches_subsequent_reads_via_L1` pakai `Sender.Send` shared scope — gagal karena `CacheInvalidationBehavior` setelah `CreateProductCommand` memanggil `requestContext.MarkMutated("products")`, lalu read-your-writes branch bypass L1. Fix: rewrite test pakai HTTP client (request boundary = scope boundary) + insert/mutate via raw SQL untuk hindari pipeline behavior.
- **Pomelo MySQL tetap unsupported**: tidak ada perubahan dari M2.5; test suite default tetap Postgres-only sesuai PRD §11 M4.

### Catatan & known limitations
- Testcontainers butuh Docker daemon di CI runner. GitHub Actions `ubuntu-latest` punya Docker pre-installed.
- Redis TIDAK di-containerize untuk test — keputusan sadar agar `HybridCacheService` test L1+L3 path. Kalau butuh validate L2 invalidation real, bisa tambah `Testcontainers.Redis` di test fixture (paket masih commented di CPM).
- Multi-provider testing (SQLite/SqlServer/MySQL parameterized) tidak di-implement di M4. Bisa jadi M4.1 follow-up jika perlu.
- `ConfigureAppConfiguration` strip JSON sources tetap berguna untuk hindari leakage `appsettings.json` dev defaults; redundan dengan DI swap tetapi defense-in-depth.

---

## ⏳ M5 — Observability & DevOps (PENDING)

Cakupan PRD §11:
- [ ] Aktifkan paket di CPM: `Serilog.AspNetCore 9.0.0`, `Serilog.Sinks.Console 6.0.0`, `OpenTelemetry.Extensions.Hosting 1.10.0`, `OpenTelemetry.Instrumentation.AspNetCore 1.10.1`, `OpenTelemetry.Instrumentation.Http 1.10.0`, `OpenTelemetry.Exporter.OpenTelemetryProtocol 1.10.0`, `AspNetCore.HealthChecks.NpgSql 8.0.2`, `AspNetCore.HealthChecks.Redis 8.0.1`
- [ ] Serilog wiring di `Program.cs` (JSON output, replace default `ILogger` provider)
- [ ] OpenTelemetry tracing + metrics, OTLP exporter ke collector
- [ ] `docker-compose.yml`: uncomment OTel collector service + config file `otel-collector-config.yaml`
- [ ] Health checks `/health/ready` (cek Postgres + Redis; Redis mati → `Degraded` bukan `Unhealthy`, sesuai filosofi graceful degradation)
- [ ] Health check `/health/startup` (opsional, terkait migrasi)
- [ ] `Dockerfile` multi-stage (Alpine atau Chiseled Ubuntu)
- [ ] `.dockerignore`
- [ ] CI: build & push container image (opsional)
- [ ] README diperbarui dengan endpoint observability + cara melihat trace di local OTel collector

---

## ⏳ M6 — (Stretch) AOT Profile (PENDING)

Cakupan PRD §11 (opsional):
- [ ] Source-generator-based mediator pengganti `MediatR` (mis. `Mediator` library atau implementasi internal)
- [ ] Source-generated endpoint registration (gantikan refleksi di `EndpointRegistrationExtensions`)
- [ ] Audit pemakaian refleksi seluruh codebase (FluentValidation discovery, EF model snapshot, JsonSerializer payload, dll.) — sediakan source-gen alternatif atau anotasi `[DynamicallyAccessedMembers]`
- [ ] Profil build `Release-AOT` dengan `<PublishAot>true</PublishAot>`
- [ ] Verifikasi `dotnet publish -c Release-AOT` produces self-contained binary
- [ ] Smoke test AOT binary di lingkungan minimal (Alpine container)

---

## 🛠️ Konfigurasi Dev Lokal

| Setting | Nilai dev (default SQLite, ADR-0001) |
|---|---|
| `Database:Provider` | `Sqlite` (default) — atau `Postgres` / `SqlServer` / `MySql` |
| `ConnectionStrings:Sqlite` | `Data Source=App_Data/pbac.db` |
| `ConnectionStrings:Postgres` | `Host=localhost;Port=5433;Database=pbac;Username=pbac;Password=pbac` |
| `ConnectionStrings:SqlServer` | `Server=localhost,1433;Database=pbac;User Id=sa;Password=Pbac!Local123;TrustServerCertificate=True` |
| `ConnectionStrings:MySql` | `Server=localhost;Port=3306;Database=pbac;User=pbac;Password=pbac` |
| `Jwt:SigningKey` | `dev-only-32-byte-jwt-signing-key-do-not-use-in-prod-1234567890` (user-secrets) |
| `Cache:RedisConnectionString` | `localhost:6379` (opsional; cache jatuh ke L1/L3 jika kosong) |

| Service | Port host | Compose profile |
|---|---|---|
| Redis | 6379 | (default — selalu up) |
| Postgres | **5433** | `--profile postgres` |
| SQL Server 2022 | 1433 | `--profile sqlserver` |
| MySQL 8.4 | 3306 | `--profile mysql` |
| API | 5000 (default) atau via `--urls` | — |

Demo user (Development only, dari `DevSeeder`):
- Email: `demo@local`
- Password: `Demo123!Demo123!`
- Permissions: `products.create`, `products.read`

## 📌 Hal yang Sengaja Ditunda (Non-Blocker)

- [ ] `Created<T>` (201) hilang setelah `ApiResponseEndpointFilter` membungkus → menjadi 200. Bisa dibuat filter lebih cerdas yang preserve status code asli.
- [ ] README belum mencakup M3 (auth flow + cache config) — akan diupdate satu kali bersama M5 docker docs.
- [ ] Migration policy production (PRD §9.7): endpoint `/admin/migrate` atau project terpisah `Api.Migrator` belum dibuat. Hanya migrasi otomatis di Development.
- [ ] Idempotency endpoint header `Idempotency-Key` (PRD §9.2) — tabel ada di skema, behavior belum.
- [ ] Multi-tenancy aktif (PRD §9.9) — eksplisit non-goal v2.1.
- [ ] Rate limiting metrics + tracing (akan jadi otomatis setelah M5 OTel).

## 🚀 Useful Commands

```powershell
# Local infra
docker compose up -d
docker compose down

# Dev tooling
dotnet tool restore
dotnet ef database update --project src\Api
dotnet ef migrations add <Name> --project src\Api --output-dir Infrastructure\Data\Migrations
dotnet ef migrations script --project src\Api --idempotent --output ./artifacts/migrate.sql

# Build & run
dotnet build
dotnet run --project src\Api --urls http://localhost:5050

# Smoke (PowerShell)
$body = '{"email":"demo@local","password":"Demo123!Demo123!"}'
$login = Invoke-WebRequest -Uri http://localhost:5050/api/v1/auth/login -Method POST -ContentType 'application/json' -Body $body -UseBasicParsing
$access = ($login.Content | ConvertFrom-Json).data.accessToken
Invoke-WebRequest -Uri http://localhost:5050/api/v1/products -Method POST `
  -ContentType 'application/json' `
  -Headers @{Authorization="Bearer $access"} `
  -Body '{"name":"Coffee","sku":"SKU-001","price":25000}' -UseBasicParsing
```
