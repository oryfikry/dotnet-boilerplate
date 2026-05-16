namespace Api.Infrastructure.Data.Entities;

/// <summary>
/// Base class for entities that carry creation/modification audit columns.
///
/// Per PRD v2.1 §9.6, <see cref="AppDbContext"/> auto-stamps these fields
/// during <c>SaveChangesAsync</c> via the ambient <c>IRequestContext</c>.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Base class for entities that participate in soft delete.
///
/// Per PRD v2.1 §9.6, the global query filter on <see cref="AppDbContext"/>
/// hides rows where <see cref="IsDeleted"/> is <c>true</c>; physical removal
/// is reserved for housekeeping jobs.
/// </summary>
public abstract class SoftDeletableEntity : AuditableEntity
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }
}
