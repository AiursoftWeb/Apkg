using System.Diagnostics.CodeAnalysis;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<MirrorRepository> MirrorRepositories => Set<MirrorRepository>();
    public DbSet<AptPackage> AptPackages => Set<AptPackage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AptPackage>(b =>
        {
            b.Property(p => p.OriginSuite).HasMaxLength(128);
            b.Property(p => p.OriginComponent).HasMaxLength(128);
            b.Property(p => p.Package).HasMaxLength(128);
            b.Property(p => p.Version).HasMaxLength(128);
            b.Property(p => p.Architecture).HasMaxLength(128);

            b.Property(p => p.Extras)
                .HasConversion(
                    v => Newtonsoft.Json.JsonConvert.SerializeObject(v),
                    v => Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(v) ??
                         new Dictionary<string, string>(),
                    new ValueComparer<Dictionary<string, string>>(
                        (c1, c2) => c1 != null && c2 != null && c1.Count == c2.Count &&
                                    c1.All(pair => c2.ContainsKey(pair.Key) && c2[pair.Key] == pair.Value),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Key.GetHashCode(), v.Value.GetHashCode())),
                        c => c.ToDictionary(entry => entry.Key, entry => entry.Value)));
        });
    }

    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();
}
