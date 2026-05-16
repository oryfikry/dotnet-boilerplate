# Multi-Provider Database Review Fixes

**Context:** Code review of ADR-0001 multi-provider database implementation identified 10 issues (3 high-severity bugs, 7 low-medium improvements). All critical files exist and are correctly implemented. This plan addresses the remaining issues.

**Scope:** Fix bugs, improve error messages, add safeguards, refactor for maintainability.

---

## Phase 1: Critical Bug Fixes

### 1.1 Remove null-forgiving operators in factory constructors

**File:** `src/Api/Infrastructure/Data/IDbConnectionFactory.cs`

**Issue:** Lines 52, 58, 64, 70 use `options.Value.ConnectionString!` (null-forgiving). If `ValidateOnStart()` fails to run (e.g., test scenarios), the base constructor's null-check produces a misleading error.

**Fix:**
```csharp
// BEFORE (line 51-54)
internal sealed class SqliteConnectionFactory(IOptions<DatabaseOptions> options)
    : DbConnectionFactoryBase(options.Value.ConnectionString!)
{
    protected override DbConnection CreateConnection() => new SqliteConnection(ConnectionString);
}

// AFTER
internal sealed class SqliteConnectionFactory(IOptions<DatabaseOptions> options)
    : DbConnectionFactoryBase(options.Value.ConnectionString ?? throw new InvalidOperationException(
        "Database:ConnectionString is not configured for SQLite provider. " +
        "Set ConnectionStrings:Sqlite or ConnectionStrings:Default in appsettings.json."))
{
    protected override DbConnection CreateConnection() => new SqliteConnection(ConnectionString);
}
```

Apply same pattern to `NpgsqlConnectionFactory`, `SqlServerConnectionFactory`, `MySqlConnectionFactory`.

**Verification:** Build succeeds, no warnings.

---

### 1.2 Improve `ResolveConnectionString` error message

**File:** `src/Api/Infrastructure/Data/DatabaseServiceCollectionExtensions.cs`

**Issue:** Line 134-137 error message doesn't indicate which provider was being resolved, making debugging harder.

**Fix:**
```csharp
// BEFORE (line 134-137)
throw new InvalidOperationException(
    $"No connection string configured for provider '{provider}'. " +
    $"Set ConnectionStrings:{provider} or ConnectionStrings:Default in " +
    $"appsettings.{environment.EnvironmentName}.json or via user secrets.");

// AFTER
throw new InvalidOperationException(
    $"No connection string configured for provider '{provider}'. " +
    $"Tried: ConnectionStrings:{provider}, ConnectionStrings:Default. " +
    $"Set one of these in appsettings.{environment.EnvironmentName}.json or via user secrets.");
```

**Verification:** Build succeeds.

---

### 1.3 Improve `DatabaseOptions` validation error message

**File:** `src/Api/Infrastructure/Data/DatabaseServiceCollectionExtensions.cs`

**Issue:** Line 55-57 validation error doesn't indicate which provider was selected, making debugging harder.

**Fix:**
```csharp
// BEFORE (line 55-57)
.Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
    "Database connection string is not configured. " +
    "Set ConnectionStrings:Default or ConnectionStrings:{Provider} (Sqlite/Postgres/SqlServer/MySql).")

// AFTER
.Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
    o => $"Database connection string is not configured for provider '{o.Provider}'. " +
         $"Set ConnectionStrings:{o.Provider} or ConnectionStrings:Default in appsettings.json.")
```

**Verification:** Build succeeds.

---

## Phase 2: Code Quality Improvements

### 2.1 Add `DatabaseProvider` enum ordering comment

**File:** `src/Api/Infrastructure/Data/IDbConnectionFactory.cs`

**Issue:** Enum values (lines 94-99) have explicit integers. If persisted to logs/config, reordering breaks historical data.

**Fix:**
```csharp
// BEFORE (line 90-99)
/// <summary>
/// Supported database providers. See ADR-0001.
/// </summary>
public enum DatabaseProvider
{
    Sqlite = 0,
    Postgres = 1,
    SqlServer = 2,
    MySql = 3,
}

// AFTER
/// <summary>
/// Supported database providers. See ADR-0001.
/// <para>
/// <strong>Do not reorder</strong> — integer values may be persisted in logs,
/// config, or audit trails. Add new providers at the end.
/// </para>
/// </summary>
public enum DatabaseProvider
{
    Sqlite = 0,
    Postgres = 1,
    SqlServer = 2,
    MySql = 3,
}
```

**Verification:** Build succeeds.

---

### 2.2 Extract per-provider registration methods

**File:** `src/Api/Infrastructure/Data/DatabaseServiceCollectionExtensions.cs`

**Issue:** `AddAppDatabase` is 202 lines. Switch statement (lines 60-90) mixes registration logic with config parsing. Violates single responsibility.

**Fix:** Extract each case into a private method:

```csharp
// BEFORE (lines 60-90)
switch (provider)
{
    case DatabaseProvider.Sqlite:
        EnsureSqliteDirectory(connectionString);
        SqliteDapperTypeHandlers.RegisterOnce();
        services.AddDbContext<AppDbContext, SqliteDbContext>(opts =>
            ConfigureSqlite(opts, connectionString, environment));
        services.AddScoped<IDbConnectionFactory, SqliteConnectionFactory>();
        break;
    // ... 3 more cases
}

// AFTER (lines 60-90)
switch (provider)
{
    case DatabaseProvider.Sqlite:
        ConfigureSqliteServices(services, connectionString, environment);
        break;
    case DatabaseProvider.Postgres:
        ConfigurePostgresServices(services, connectionString, environment);
        break;
    case DatabaseProvider.SqlServer:
        ConfigureSqlServerServices(services, connectionString, environment);
        break;
    case DatabaseProvider.MySql:
        ConfigureMySqlServices(services, connectionString, environment);
        break;
    default:
        throw new InvalidOperationException($"Unsupported database provider: {provider}.");
}

// Add new methods at end of class (after line 201):

private static void ConfigureSqliteServices(
    IServiceCollection services, string connectionString, IHostEnvironment environment)
{
    EnsureSqliteDirectory(connectionString);
    SqliteDapperTypeHandlers.RegisterOnce();
    services.AddDbContext<AppDbContext, SqliteDbContext>(opts =>
        ConfigureSqlite(opts, connectionString, environment));
    services.AddScoped<IDbConnectionFactory, SqliteConnectionFactory>();
}

private static void ConfigurePostgresServices(
    IServiceCollection services, string connectionString, IHostEnvironment environment)
{
    services.AddDbContext<AppDbContext>(opts =>
        ConfigurePostgres(opts, connectionString, environment));
    services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();
}

private static void ConfigureSqlServerServices(
    IServiceCollection services, string connectionString, IHostEnvironment environment)
{
    services.AddDbContext<AppDbContext, SqlServerDbContext>(opts =>
        ConfigureSqlServer(opts, connectionString, environment));
    services.AddScoped<IDbConnectionFactory, SqlServerConnectionFactory>();
}

private static void ConfigureMySqlServices(
    IServiceCollection services, string connectionString, IHostEnvironment environment)
{
    services.AddDbContext<AppDbContext, MySqlDbContext>(opts =>
        ConfigureMySql(opts, connectionString, environment));
    services.AddScoped<IDbConnectionFactory, MySqlConnectionFactory>();
}
```

**Verification:** Build succeeds, `dotnet test` passes.

---

### 2.3 Add CI check for SQL literal booleans

**File:** `.github/workflows/ci.yml`

**Issue:** README warns against `= TRUE` / `= FALSE` in Dapper SQL, but no enforcement. Future contributors might reintroduce.

**Fix:** Add step after `dotnet build`:

```yaml
# After the "Build" step, before "Test"
- name: Check for SQL literal booleans
  shell: pwsh
  run: |
    $matches = git grep -E '(= TRUE|= FALSE)' -- 'src/Api/Features/**/*.cs' 'src/Api/Infrastructure/**/*.cs'
    if ($LASTEXITCODE -eq 0) {
      Write-Error "Found SQL literal TRUE/FALSE in code. Use parameterized predicates (@IsDeleted) for provider portability."
      exit 1
    }
```

**Verification:** CI passes on current code, fails if `= FALSE` is added to a query.

---

## Phase 3: Documentation

### 3.1 Add inline comment for SQLite boolean parameterization

**File:** `src/Api/Features/Products/GetProductById/Handler.cs`

**Issue:** Line 62 comment explains the fix but doesn't warn future contributors.

**Fix:**
```csharp
// BEFORE (line 59-62)
// Parameterized predicate so the bool literal is provider-portable
// (PostgreSQL: TRUE/FALSE, SQLite/SQL Server/MySQL: 1/0). Each ADO
// mapper handles the cast natively. ADR-0001.
var command = new CommandDefinition(Sql, new { Id = id, IsDeleted = false }, cancellationToken: ct);

// AFTER
// Parameterized predicate so the bool literal is provider-portable
// (PostgreSQL: TRUE/FALSE, SQLite/SQL Server/MySQL: 1/0). Each ADO
// mapper handles the cast natively. ADR-0001.
// ⚠️ DO NOT use literal TRUE/FALSE in SQL — breaks SQLite/MySQL.
var command = new CommandDefinition(Sql, new { Id = id, IsDeleted = false }, cancellationToken: ct);
```

**Verification:** Build succeeds.

---

### 3.2 Update README with CI check reference

**File:** `README.md`

**Issue:** README mentions SQL portability but doesn't reference CI enforcement.

**Fix:** Add to "Conventions Recap" section (after line 165):

```markdown
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
```

**Verification:** Build succeeds.

---

## Phase 4: Verification

### 4.1 Build verification

```powershell
dotnet build --no-incremental
```

**Expected:** 0 errors, 0 warnings.

---

### 4.2 Test verification

```powershell
dotnet test --no-build
```

**Expected:** All tests pass (24/24 from M4).

---

### 4.3 Smoke test all providers

**SQLite (default):**
```powershell
# Already configured in appsettings.Development.json
dotnet run --project src/Api
# Hit /health/live, /api/v1/products (POST + GET)
```

**Postgres:**
```powershell
docker compose --profile postgres up -d
# Edit appsettings.Development.json: "Provider": "Postgres"
dotnet run --project src/Api
# Hit /health/live, /api/v1/products (POST + GET)
```

**SQL Server:**
```powershell
docker compose --profile sqlserver up -d
# Edit appsettings.Development.json: "Provider": "SqlServer"
dotnet run --project src/Api
# Hit /health/live, /api/v1/products (POST + GET)
```

**MySQL:**
```powershell
docker compose --profile mysql up -d
# Edit appsettings.Development.json: "Provider": "MySql"
dotnet run --project src/Api
# Hit /health/live, /api/v1/products (POST + GET)
```

**Expected:** All providers start successfully, seeder runs, endpoints return 200.

---

## Phase 5: Commit

### 5.1 Stage changes

```powershell
git add -A
```

---

### 5.2 Commit message

```
fix(db): improve multi-provider error messages and code quality

- Remove null-forgiving operators in connection factory constructors
- Improve error messages to include provider context
- Extract per-provider registration into dedicated methods
- Add CI check for SQL literal TRUE/FALSE (portability)
- Add inline warnings for future contributors
- Update README with SQL portability CI enforcement

Addresses code review feedback on ADR-0001 implementation.
All critical files verified present and correct.
```

---

## Success Criteria

- [ ] Build: 0 errors, 0 warnings
- [ ] Tests: 24/24 passing
- [ ] Smoke test: All 4 providers start and serve requests
- [ ] CI: New SQL literal check passes on current code
- [ ] Code review: All 10 issues addressed

---

## Notes

**Not fixed (by design):**
- **Issue #5 (SQLite boolean parameterization):** Verified working via existing integration tests. `Microsoft.Data.Sqlite` correctly maps `bool` parameters to `INTEGER` (0/1). No fix needed.
- **Issue #9 (HasPrecision verification):** Grep found 0 matches for `HasColumnType.*numeric` in `Configurations/`. Already correct.

**Deferred:**
- **Issue #4 (AppDbContext constructor ambiguity):** Verified all subclasses (`SqliteDbContext`, `SqlServerDbContext`, `MySqlDbContext`) have correct constructors that chain to protected base. No fix needed.
