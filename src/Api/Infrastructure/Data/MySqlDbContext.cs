using Api.Common.Context;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time <see cref="DbContext"/> bound to the MySQL provider
/// (Oracle MySql.EntityFrameworkCore — Pomelo has no EF10 release as of 2026-05).
/// Migrations live under <c>Infrastructure/Data/Migrations/MySql/</c>.
/// See ADR-0001.
/// </summary>
public sealed class MySqlDbContext : AppDbContext
{
    public MySqlDbContext(DbContextOptions<MySqlDbContext> options, IRequestContext requestContext)
        : base(options, requestContext)
    {
    }
}
