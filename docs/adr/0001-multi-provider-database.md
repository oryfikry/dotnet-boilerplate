# 0001. Multi-provider database (SQLite default, Postgres / SQL Server / MySQL opt-in)

Status: Accepted
Date: 2026-05-16
Supersedes: PRD v2.1 §3.1 (PostgreSQL-only stance)

## Context

PRD v2.1 §3.1 locked PostgreSQL 16+ as the sole relational provider. This is correct
for production-grade deployments but creates real friction:

- **Onboarding friction.** New contributors must install Docker and run
  `docker compose up -d` before they can `dotnet run`. The README quickstart
  has 5 prerequisite steps before any code runs.
- **Demos and ad-hoc testing.** A boilerplate is also a teaching artefact.
  Forcing Postgres on every reader inflates the "first byte" cost.
- **Heterogeneous deployment targets.** Real users land on the BCL with whichever
  enterprise database they already operate. Hard-coding Postgres makes the
  boilerplate non-adoptable for SQL Server / MySQL shops without a fork.

The user requested SQLite-by-default with easy provider switching. This ADR
captures the decision and its trade-offs.

## Decision

**SQLite is the default provider.** The boilerplate ships first-class support
for four providers, selected at startup via the `Database:Provider`
configuration key:

| Provider     | Value         | Default? | Notes |
|---|---|---|---|
| SQLite       | `Sqlite`      | ✅       | Zero infra. Stores at `App_Data/pbac.db` by default. |
| PostgreSQL   | `Postgres`    |          | Original PRD v2.1 target. Existing migrations preserved. |
| SQL Server   | `SqlServer`   |          | docker-compose service under `--profile sqlserver`. |
| MySQL        | `MySql`       |          | docker-compose service under `--profile mysql`. Uses Oracle's `MySql.EntityFrameworkCore` (Pomelo has no EF10 release as of 2026-05). |

Connection strings live under `ConnectionStrings:{Provider}` with optional
`ConnectionStrings:Default` fallback.

### Architecture

1. **Single runtime `AppDbContext`.** All handlers, behaviors, and the seeder
   take `AppDbContext`. They are provider-agnostic.
2. **Per-provider design-time subclasses.** `SqliteDbContext`,
   `SqlServerDbContext`, `MySqlDbContext` each derive from `AppDbContext` and
   exist solely so the EF Core CLI can generate independent migrations and
   model snapshots without collisions in a single assembly. PostgreSQL stays
   bound to the base `AppDbContext` to preserve the historical migration
   `20260516074807_Initial`.
3. **Per-provider migration folders.** `Infrastructure/Data/Migrations/Sqlite/`,
   `.../SqlServer/`, `.../MySql/`. PostgreSQL keeps its existing flat layout at
   `Infrastructure/Data/Migrations/`.
4. **Single `IDbConnectionFactory` interface, four implementations.**
   `SqliteConnectionFactory`, `NpgsqlConnectionFactory`, `SqlServerConnectionFactory`,
   `MySqlConnectionFactory`. Selected at startup.
5. **`AddAppDatabase()` extension** in
   `Infrastructure/Data/DatabaseServiceCollectionExtensions.cs` reads
   `Database:Provider`, resolves the connection string, and registers the
   matching `DbContext` + `IDbConnectionFactory`.
6. **Provider-neutral entity configuration.** `EntityConfigurations` uses
   `HasPrecision(18, 4)` instead of PG-typed `numeric(18,4)`. EF emits the
   right SQL per provider.
7. **Provider-portable Dapper SQL.** `is_deleted = @IsDeleted` parameter
   instead of `is_deleted = FALSE`. Each ADO mapper handles the bool
   coercion natively.
8. **SQLite Dapper type handlers.** Microsoft.Data.Sqlite stores `Guid`,
   `DateTime`, and `decimal` as TEXT/INTEGER. Dapper's default mapper cannot
   coerce TEXT back to those CLR types when materializing record-style DTOs
   via positional constructors. `SqliteDapperTypeHandlers.RegisterOnce()` is
   invoked once at startup whenever the SQLite provider is selected, adding
   global `SqlMapper.TypeHandler<T>` registrations for `Guid`, `Guid?`,
   `DateTime`, `DateTime?`, `decimal`, `decimal?`. ISO-8601 / invariant-string
   round-trip is portable across providers, so the handlers are safe to leave
   in place even if the same process later reads from a non-SQLite source.

### Package versions

All Microsoft EF packages pinned to `10.0.8` (current 10.0.x patch as of
2026-05-12). Provider matrix:

| Package | Version |
|---|---|
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.8 |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.8 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 |
| MySql.EntityFrameworkCore | 10.0.7 |
| Microsoft.Data.Sqlite | 10.0.8 |
| Microsoft.Data.SqlClient | 7.0.0 |
| Npgsql | 10.0.0 |
| MySqlConnector | 2.5.0 |

Pomelo `Pomelo.EntityFrameworkCore.MySql` is **not** EF10-ready as of 2026-05;
its latest release (`9.0.0`, 2025-08-17) targets EF Core 9. We use Oracle's
`MySql.EntityFrameworkCore` until Pomelo ships an EF10-compatible release.

### docker-compose

Postgres / SQL Server / MySQL services moved behind compose `profiles`
(`postgres`, `sqlserver`, `mysql`). Default `docker compose up -d` brings up
only Redis (the M3 cache). SQLite needs no service.

## Consequences

### Positive

- **Zero-config quickstart.** `dotnet run` works out of the box. No Docker
  required for first contact.
- **Pluggable.** Anyone can switch providers via a single appsettings line,
  with no recompile.
- **Demo parity.** Same JWT/permission/cache flows run on all four providers.
- **Tests stay deterministic.** SQLite is excellent for fast in-process
  integration tests. Testcontainers Postgres remains the canonical CI target
  (PRD §11 M4).

### Negative

- **More packages, more surface.** Four EF providers and four ADO drivers ship
  even if a deployment uses only one. Acceptable for a boilerplate; AOT users
  may want to trim.
- **Multiple migration sets.** Each schema change requires
  `dotnet ef migrations add` four times (Sqlite / SqlServer / MySql /
  AppDbContext for Postgres). Mitigated by the boilerplate's small entity set.
- **Per-provider SQL portability is the developer's responsibility.** Dapper
  hand-written queries must avoid PG-only literals (`FALSE`, `::cast`,
  `RETURNING`). The boilerplate's only Dapper query is `GetProductByIdHandler`
  and now uses fully-parameterized SQL.
- **SQLite has loose typing.** Dates, GUIDs, decimals round-trip through TEXT;
  the type handlers compensate. A column with `HasColumnType("numeric(18,4)")`
  in SQLite still stores TEXT, so always prefer `HasPrecision(...)`.
- **EF10 stopped wrapping all migrations in one transaction.** Per-migration
  txns are still atomic, but a multi-migration `MigrateAsync` can leave the
  schema partially applied if one fails. Document in production runbooks.
- **PRD v2.2 supersedes v2.1.** §3.1 stack table updated; §11 milestones list
  the "Multi-provider DB" deliverable as part of M2 closure.

## Alternatives considered

1. **SQLite-only.** Rejected — drops the existing Postgres/Redis/Polly story
   that the rest of the boilerplate is built around.
2. **Two providers (SQLite + Postgres).** Rejected by user request for full
   pluggability.
3. **Single migrations folder, switch at runtime via reflection.** Rejected —
   EF Core's design-time tooling needs concrete `IDesignTimeDbContextFactory`
   targets per provider for `dotnet ef migrations add` to work cleanly.
4. **Pomelo for MySQL.** Rejected — no EF10 release as of 2026-05.

## Implementation checklist

- [x] Add four EF + four ADO providers to CPM (`Directory.Packages.props`)
- [x] Add `DatabaseProvider` enum, refactor `IDbConnectionFactory`
- [x] Add `DatabaseServiceCollectionExtensions.AddAppDatabase()`
- [x] Add per-provider design-time subclasses + factories
- [x] Make `EntityConfigurations` provider-neutral (`HasPrecision`)
- [x] Make `GetProductByIdHandler` Dapper SQL parameterized
- [x] Add `SqliteDapperTypeHandlers` (Guid/DateTime/decimal)
- [x] Generate `Migrations/Sqlite/Initial`, `.../SqlServer/Initial`, `.../MySql/Initial`
- [x] Preserve existing Postgres `Migrations/20260516074807_Initial`
- [x] Switch DevSeeder from `EnsureCreatedAsync` to `MigrateAsync`
- [x] Update `appsettings(.Development).json` with `Database:Provider` + `ConnectionStrings`
- [x] docker-compose: profile-gate Postgres/SqlServer/MySql; Redis stays default
- [x] Bump PRD → v2.2
- [x] README + todo.md
- [x] Smoke test SQLite end-to-end (anon→401, login, create, get, cache hit, refresh, replay→401)
