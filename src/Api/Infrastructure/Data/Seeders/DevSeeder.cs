using Api.Infrastructure.Data.Entities;
using Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure.Data.Seeders;

/// <summary>
/// Seeds Permissions and a demo user for the <c>Development</c> environment.
///
/// PRD v2.2 §9.7 — production migrations are managed out of band, and the
/// seeder runs only when <c>IHostEnvironment.IsDevelopment()</c> is true.
///
/// In Development the seeder also applies any pending EF Core migrations
/// for the configured provider (ADR-0001).
/// </summary>
internal static class DevSeeder
{
    public const string DemoEmail = "demo@local";
    public const string DemoPassword = "Demo123!Demo123!";

    public static readonly string[] DefaultPermissions =
    [
        "products.create",
        "products.read"
    ];

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, CancellationToken ct = default)
    {
        // Apply any pending migrations for the configured provider. Each
        // provider has its own migrations folder bound to its own DbContext
        // subclass — see ADR-0001.
        await db.Database.MigrateAsync(ct);

        // Permissions
        var existingPermNames = await db.Permissions
            .Select(p => p.Name)
            .ToListAsync(ct);

        var newPerms = DefaultPermissions
            .Except(existingPermNames, StringComparer.OrdinalIgnoreCase)
            .Select(name => new Permission
            {
                Id = Guid.CreateVersion7(),
                Name = name,
                Description = $"Auto-seeded permission: {name}"
            })
            .ToArray();

        if (newPerms.Length > 0)
        {
            db.Permissions.AddRange(newPerms);
            await db.SaveChangesAsync(ct);
        }

        // Demo user
        var demoExists = await db.Users.AnyAsync(u => u.Email == DemoEmail, ct);
        if (!demoExists)
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                Email = DemoEmail,
                PasswordHash = hasher.Hash(DemoPassword),
                IsActive = true
            };
            db.Users.Add(user);

            var allPerms = await db.Permissions
                .Where(p => DefaultPermissions.Contains(p.Name))
                .ToListAsync(ct);

            foreach (var p in allPerms)
            {
                db.UserPermissions.Add(new UserPermission
                {
                    UserId = user.Id,
                    PermissionId = p.Id
                });
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
