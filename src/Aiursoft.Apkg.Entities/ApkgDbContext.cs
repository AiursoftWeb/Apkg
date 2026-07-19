using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para>Central database context for the Apkg platform — an APT repository server +
/// CLI build toolchain. Owns all tables and their relationships.</para>
///
/// <para><b>Architecture (see docs/design.md §2):</b></para>
/// <code>
///   Upstream (Ubuntu) → AptMirror → AptBucket → AptRepository → APT Client
///                                        ↑
///                                 ApkgDebPackage (user override)
///
///   ApkgPackage → ApkgRevision → ApkgDebPackage (upload pipeline)
/// </code>
///
/// <para><b>Naming convention (docs/design.md §16):</b></para>
/// <list type="bullet">
///   <item><b>Apt*</b> — low-level APT protocol concepts (Mirror, Repository, Bucket, Package, Certificate)</item>
///   <item><b>Apkg*</b> — platform-level abstractions (Package family, Revision, upload)</item>
/// </list>
///
/// <para><b>Table summary:</b></para>
/// <list type="table">
///   <listheader><term>Table</term><description>Purpose</description></listheader>
///   <item><term><see cref="GlobalSetting"/></term><description>Key-value application configuration</description></item>
///   <item><term><see cref="User"/></term><description>User accounts (extends IdentityUser)</description></item>
///   <item><term><see cref="UserApiKey"/></term><description>API keys for CLI/CI auth</description></item>
///   <item><term><see cref="AptCertificate"/></term><description>GPG signing keys for repositories</description></item>
///   <item><term><see cref="AptMirror"/></term><description>Upstream APT mirror (data ingress)</description></item>
///   <item><term><see cref="AptRepository"/></term><description>APT repository served to clients (data egress)</description></item>
///   <item><term><see cref="AptBucket"/></term><description>Immutable versioned snapshot of package metadata</description></item>
///   <item><term><see cref="AptPackage"/></term><description>Single package record inside a bucket</description></item>
///   <item><term><see cref="ApkgDebPackage"/></term><description>User-uploaded .deb override</description></item>
///   <item><term><see cref="ApkgPackage"/></term><description>Package family by (Name, Distro, Component)</description></item>
///   <item><term><see cref="ApkgRevision"/></term><description>Single <c>apkg push</c> event</description></item>
///   <item><term><see cref="DependencyCheckReport"/></term><description>Dependency completeness check result</description></item>
/// </list>
///
/// <para><b>Foreign key relationships (defined in <see cref="OnModelCreating"/>):</b></para>
/// <list type="bullet">
///   <item>ApkgPackage 1→N ApkgRevision (Cascade: deleting a package removes all revisions)</item>
///   <item>ApkgRevision 1→N ApkgDebPackage (Cascade: deleting a revision removes its packages)</item>
/// </list>
/// <para>All other relationships (AptMirror→Bucket, AptRepository→Bucket, etc.) are
/// simple FK properties without explicit cascade configuration — they default to
/// Restrict or are nullable.</para>
///
/// <para><b>Database design notes:</b></para>
/// <list type="bullet">
///   <item><b>Dual-bucket pattern</b>: AptMirror and AptRepository each hold PrimaryBucketId + SecondaryBucketId
///   to implement atomic snapshot swaps without exposing half-built state to APT clients (design.md §15.4).</item>
///   <item><b>SHA256 CAS</b>: .deb files are stored content-addressed at <c>Objects/{sha256[..2]}/{sha256}.deb</c>.
///   GarbageCollectionJob uses SHA256 reference counting across AptPackage and ApkgDebPackage to decide deletions.</item>
///   <item><b>IsVirtual lazy download</b>: AptPackage.IsVirtual tracks whether the binary is in CAS.
///   First download triggers fetch from RemoteUrl; subsequent requests hit the fast path.</item>
///   <item><b>Override semantics</b>: ApkgDebPackage replaces all upstream AptPackages with the same
///   (Package, Architecture) during RepositorySyncJob, regardless of version.</item>
/// </list>
/// </summary>
public abstract class ApkgDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    /// <summary>
    /// <b>GlobalSettings</b> — Simple key-value store for application-wide settings
    /// (maintenance mode, feature flags). <br/>
    /// <b>3NF:</b> ✅ Two-column key-value table; no transitive dependencies possible. <br/>
    /// <b>Lifecycle:</b> Seeded at startup → read at runtime → updated via admin UI.
    /// </summary>
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();

    /// <summary>
    /// <b>AptMirrors</b> — Upstream APT repositories (e.g., Ubuntu archive). Data ingress point. <br/>
    /// <b>3NF:</b> ✅ All monitoring fields (LastPullTime, etc.) describe the mirror directly. <br/>
    /// <b>Lifecycle:</b> Created → MirrorSyncJob pulls upstream → Primary/Secondary bucket rotation.
    /// </summary>
    public DbSet<AptMirror> AptMirrors => Set<AptMirror>();

    /// <summary>
    /// <b>AptRepositories</b> — APT repositories served to end-user clients. Data egress point. <br/>
    /// <b>3NF:</b> ✅ AllowAnyoneToUpload, EnableGpgSign are independent attributes of the repo. <br/>
    /// <b>Lifecycle:</b> Created → RepositorySyncJob builds bucket → RepositorySignJob signs → APT clients consume.
    /// </summary>
    public DbSet<AptRepository> AptRepositories => Set<AptRepository>();

    /// <summary>
    /// <b>AptBuckets</b> — Immutable versioned snapshots of package metadata at a point in time. <br/>
    /// <b>3NF:</b> ✅ ReleaseContent and InReleaseContent are independent representations of bucket metadata. <br/>
    /// <b>Lifecycle:</b> Created by sync → populated with packages → signed → promoted → orphaned → GC'd.
    /// </summary>
    public DbSet<AptBucket> AptBuckets => Set<AptBucket>();

    /// <summary>
    /// <b>AptPackages</b> — Single package record inside an AptBucket. Inherits DebianPackage for
    /// full APT metadata. <br/>
    /// <b>3NF:</b> ✅ Component stored here because a bucket can contain multiple components.
    /// IsVirtual depends on external CAS state, not on other columns. <br/>
    /// <b>Lifecycle:</b> Created during sync (IsVirtual=true) → downloaded on first request → IsVirtual=false → GC'd with bucket.
    /// </summary>
    public DbSet<AptPackage> AptPackages => Set<AptPackage>();

    /// <summary>
    /// <b>AptCertificates</b> — GPG signing keys. Each repository can have its own certificate. <br/>
    /// <b>3NF:</b> ✅ PublicKey, PrivateKey, Fingerprint describe the same key independently. <br/>
    /// <b>Lifecycle:</b> Created/admin-seeded → associated with repositories → used by RepositorySignJob.
    /// </summary>
    public DbSet<AptCertificate> AptCertificates => Set<AptCertificate>();

    /// <summary>
    /// <b>ApkgPackages</b> — Immutable package family identified by the "three dead attributes":
    /// (Name, Distro, Component). First push claims ownership. <br/>
    /// <b>3NF:</b> ✅ Unique index on (Name, Distro, Component). Metadata fields describe the family. <br/>
    /// <b>Lifecycle:</b> Created on first push → accumulates revisions → never deleted in normal operation.
    /// </summary>
    public DbSet<ApkgPackage> ApkgPackages => Set<ApkgPackage>();

    /// <summary>
    /// <b>ApkgRevisions</b> — One row per <c>apkg push</c>. Records uploader, file, and publish status. <br/>
    /// <b>3NF:</b> ✅ IsListed and TempApkgFileInVaultPath are independent attributes. No transitive dependencies. <br/>
    /// <b>Lifecycle:</b> Created during push → debs uploaded → temp file cleared on publish → temp cleanup if abandoned (30min).
    /// </summary>
    public DbSet<ApkgRevision> ApkgRevisions => Set<ApkgRevision>();

    /// <summary>
    /// <b>ApkgDebPackages</b> — User-uploaded .deb overrides. The "final word" over upstream packages. <br/>
    /// <b>3NF:</b> ✅ APT metadata extracted from .deb file; SHA256 computed from binary. All depend on PK. <br/>
    /// <b>Lifecycle:</b> Uploaded → enabled → RepositorySyncJob copies to AptPackage → disable/delete.
    /// </summary>
    public DbSet<ApkgDebPackage> ApkgDebPackages => Set<ApkgDebPackage>();

    /// <summary>
    /// <b>UserApiKeys</b> — API keys for CLI/CI. Only SHA-256 hash stored; raw key shown once. <br/>
    /// <b>3NF:</b> ✅ IsExpired is computed from ExpiresAt (not stored). KeyHash unique index. <br/>
    /// <b>Lifecycle:</b> Created → used via Authorization header → expires or revoked.
    /// </summary>
    public DbSet<UserApiKey> UserApiKeys => Set<UserApiKey>();

    /// <summary>
    /// <b>DependencyCheckReports</b> — Result of repository dependency completeness check. <br/>
    /// <b>3NF:</b> ✅ ProblematicPackages is a summary count derived at write time (controlled duplication). <br/>
    /// <b>Lifecycle:</b> Manually triggered → Running → Completed/Failed → expires after 72h.
    /// </summary>
    public DbSet<DependencyCheckReport> DependencyCheckReports => Set<DependencyCheckReport>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApkgPackage>()
            .HasOne(p => p.OwnerUser)
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ApkgPackage>()
            .HasMany(p => p.Revisions)
            .WithOne(r => r.ApkgPackage)
            .HasForeignKey(r => r.ApkgPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ApkgRevision>()
            .HasMany(r => r.ApkgDebPackages)
            .WithOne(lp => lp.ApkgRevision)
            .HasForeignKey(lp => lp.ApkgRevisionId)
            .OnDelete(DeleteBehavior.Cascade);

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

    public virtual Task MigrateAsync(CancellationToken cancellationToken)
    {
        // DDL operations like ALTER TABLE MODIFY COLUMN on large tables can take
        // much longer than the default 30s command timeout (full table rebuild).
        Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        return Database.MigrateAsync(cancellationToken);
    }

    public virtual Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();
}
