using Api.Common.Context;
using Api.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Api.Infrastructure.Data;

/// <summary>
/// Application <see cref="DbContext"/>.
///
/// Per PRD v2.2 §6 directive #3, EF Core handles <em>command</em> paths
/// (insert / update / delete). Read-side queries go through
/// <see cref="IDbConnectionFactory"/> (Dapper).
///
/// Audit columns (<see cref="AuditableEntity.CreatedAtUtc"/>, etc.) and the
/// soft-delete flag (<see cref="SoftDeletableEntity.IsDeleted"/>) are
/// auto-stamped during <see cref="SaveChangesAsync"/> using the ambient
/// <see cref="IRequestContext"/> — see PRD §9.6.
///
/// This class is <strong>not sealed</strong> so per-provider design-time
/// subclasses (<c>SqliteDbContext</c>, <c>SqlServerDbContext</c>,
/// <c>MySqlDbContext</c>) can bind their own EF Core migrations and model
/// snapshots while sharing the same runtime model — see ADR-0001.
/// </summary>
public class AppDbContext : DbContext
{
    private readonly IRequestContext _requestContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, IRequestContext requestContext)
        : base(options)
    {
        _requestContext = requestContext;
    }

    /// <summary>
    /// Constructor for per-provider design-time subclasses (ADR-0001).
    /// Accepts a non-generic <see cref="DbContextOptions"/> so a subclass with
    /// its own <c>DbContextOptions&lt;TSubclass&gt;</c> can chain through.
    /// </summary>
    protected AppDbContext(DbContextOptions options, IRequestContext requestContext)
        : base(options)
    {
        _requestContext = requestContext;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAuditFields()
    {
        var nowUtc = DateTime.UtcNow;
        var actor = _requestContext.UserId;

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity is AuditableEntity auditable:
                    auditable.CreatedAtUtc = nowUtc;
                    auditable.CreatedBy ??= actor;
                    break;

                case EntityState.Modified when entry.Entity is AuditableEntity auditable:
                    auditable.UpdatedAtUtc = nowUtc;
                    auditable.UpdatedBy ??= actor;
                    HandleSoftDelete(entry, auditable, nowUtc, actor);
                    break;

                case EntityState.Deleted when entry.Entity is SoftDeletableEntity soft:
                    // Convert hard-delete to soft-delete (PRD §9.6).
                    entry.State = EntityState.Modified;
                    soft.IsDeleted = true;
                    soft.DeletedAtUtc = nowUtc;
                    soft.DeletedBy = actor;
                    soft.UpdatedAtUtc = nowUtc;
                    soft.UpdatedBy = actor;
                    break;
            }
        }
    }

    private static void HandleSoftDelete(
        EntityEntry entry, AuditableEntity entity, DateTime nowUtc, Guid? actor)
    {
        if (entity is not SoftDeletableEntity soft) return;

        var prop = entry.Property(nameof(SoftDeletableEntity.IsDeleted));
        if (!prop.IsModified) return;

        if (soft.IsDeleted && soft.DeletedAtUtc is null)
        {
            soft.DeletedAtUtc = nowUtc;
            soft.DeletedBy ??= actor;
        }
    }
}
