using Api.Common.Context;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time <see cref="DbContext"/> bound to the SQLite provider.
///
/// EF Core's CLI uses this concrete type plus its model snapshot to produce
/// migrations under <c>Infrastructure/Data/Migrations/Sqlite/</c> without
/// colliding with the snapshots of the other providers (ADR-0001).
///
/// At runtime the application registers <see cref="AppDbContext"/> as the
/// service type and constructs an instance of this subclass when
/// <c>Database:Provider = Sqlite</c>.
/// </summary>
public sealed class SqliteDbContext : AppDbContext
{
    public SqliteDbContext(DbContextOptions<SqliteDbContext> options, IRequestContext requestContext)
        : base(options, requestContext)
    {
    }
}
