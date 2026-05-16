using Api.Common.Context;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time <see cref="DbContext"/> bound to the SQL Server provider.
/// Migrations live under <c>Infrastructure/Data/Migrations/SqlServer/</c>.
/// See ADR-0001.
/// </summary>
public sealed class SqlServerDbContext : AppDbContext
{
    public SqlServerDbContext(DbContextOptions<SqlServerDbContext> options, IRequestContext requestContext)
        : base(options, requestContext)
    {
    }
}
