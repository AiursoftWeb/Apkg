using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Aiursoft.Apkg.Entities;

public abstract class ApkgDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<AptMirror> AptMirrors => Set<AptMirror>();
    public DbSet<AptRepository> AptRepositories => Set<AptRepository>();
    public DbSet<AptBucket> AptBuckets => Set<AptBucket>();
    public DbSet<AptPackage> AptPackages => Set<AptPackage>();
    public DbSet<AptCertificate> AptCertificates => Set<AptCertificate>();
    public DbSet<LocalPackage> LocalPackages => Set<LocalPackage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AptPackage>()
            .Property(p => p.Extras)
            .HasConversion(
                v => Newtonsoft.Json.JsonConvert.SerializeObject(v),
                v => Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(v) ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (c1, c2) => c1 != null && c2 != null && c1.Count == c2.Count && c1.All(pair => c2.ContainsKey(pair.Key) && c2[pair.Key] == pair.Value),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Key.GetHashCode(), v.Value.GetHashCode())),
                    c => c.ToDictionary(entry => entry.Key, entry => entry.Value)));
    }

    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();
}
