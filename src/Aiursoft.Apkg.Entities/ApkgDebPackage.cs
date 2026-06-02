using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>ApkgDebPackage</b> — A .deb file extracted from an ApkgRevision and uploaded to a repository.</para>
///
/// <para><b>Design purpose:</b> Represents the "final word" over upstream mirror packages.
/// Within a <see cref="AptRepository"/>, an enabled ApkgDebPackage replaces ALL upstream
/// packages with the same (Package, Architecture) — regardless of version, regardless of
/// component. This is the mechanism by which AnduinOS replaces Ubuntu packages with
/// customized versions (see docs/design.md §5).</para>
///
/// <para><b>Override semantics (see docs/design.md §5):</b></para>
/// <list type="bullet">
///   <item>Override is by (Package, Architecture) — amd64 override does NOT affect arm64.</item>
///   <item>Version is irrelevant for matching: v1.0 of a ApkgDebPackage overrides v99.0 of
///   an upstream package.</item>
///   <item>IsEnabled=false packages are skipped by RepositorySyncJob — effectively removed.</item>
///   <item>Only one active ApkgDebPackage per (RepositoryId, Package, Version, Architecture) slot.
///   Component is intentionally excluded from slot dedup — a package cannot coexist in
///   multiple components within the same repository. Attempting to push the same package
///   with a different Component will result in a 409 Conflict.</item>
/// </list>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Uploaded via Web UI (standalone .deb) or via <c>apkg push</c> (linked to ApkgRevision).</item>
///   <item>.deb file parsed → APT metadata extracted → stored on disk at
///   <c>ApkgDebPackages/{repositoryId}/{package}_{version}_{arch}.deb</c>.</item>
///   <item>SHA256 computed server-side from the .deb file (authoritative).</item>
///   <item>RepositorySyncJob copies enabled ApkgDebPackages into the new AptBucket as
///   AptPackage entries (IsVirtual=false, RemoteUrl=null, Origin="ApkgDebPackage").</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Uploaded</b>: .deb received, parsed, stored. IsEnabled=true.</item>
///   <item><b>Downgrade check</b> (upload time): If version is lower than the current
///   Primary Bucket's version for the same (Package, Architecture) → 403 Forbidden
///   (unless <c>allowDowngrade=true</c>).</item>
///   <item><b>Active</b>: RepositorySyncJob copies it into the new bucket. APT clients
///   see it as a normal package.</item>
///   <item><b>Disabled</b>: IsEnabled=false → skipped by sync (effectively removed from
///   the repository on next sync).</item>
///   <item><b>Superseded</b>: New version uploaded for same slot → old version
///   soft-disabled.</item>
/// </list>
///
/// <para><b>Relationships:</b></para>
/// <list type="bullet">
///   <item>Belongs to <see cref="AptRepository"/> via RepositoryId (many-to-one).</item>
///   <item>Optionally linked to <see cref="ApkgRevision"/> via ApkgRevisionId
///   (nullable — standalone .deb uploads have no revision).</item>
///   <item>Has uploader <see cref="User"/> via UploadedByUserId.</item>
///   <item>Index: <c>(RepositoryId, Package, Architecture)</c> for slot-lookup.
///   Component is intentionally excluded — see slot dedup note above.</item>
/// </list>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic.</item>
///   <item><b>2NF ✅</b> — PK is Id. All fields depend on the full PK.</item>
///   <item><b>3NF ✅</b> — No transitive dependencies. APT metadata fields (Version,
///   Maintainer, Depends, etc.) are extracted from the .deb file and all depend directly
///   on the ApkgDebPackage. SHA256 is computed from the binary, not derived from other columns.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
[Index(nameof(RepositoryId), nameof(Package), nameof(Architecture))]
[Index(nameof(UploadedByUserId))]
public class ApkgDebPackage
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedByUser { get; set; }

    /// <summary>
    /// FK to the ApkgRevision that uploaded this package. Null for standalone
    /// .deb uploads (not via apkg push).
    /// </summary>
    public int? ApkgRevisionId { get; set; }

    [ForeignKey(nameof(ApkgRevisionId))]
    public ApkgRevision? ApkgRevision { get; set; }

    public int RepositoryId { get; set; }

    [ForeignKey(nameof(RepositoryId))]
    public AptRepository? Repository { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// False = skipped by RepositorySyncJob. Used for soft-delete and version
    /// superseding (old version disabled when new one is uploaded).
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // ── APT metadata (extracted from .deb control file) ──

    [Required]
    [MaxLength(128)]
    public required string Package { get; set; }

    [Required]
    [MaxLength(128)]
    public required string Version { get; set; }

    [Required]
    [MaxLength(128)]
    public required string Architecture { get; set; }

    public required string Maintainer { get; set; }
    public string? Description { get; set; }
    public string? Section { get; set; }
    public string? Priority { get; set; }
    public string? Homepage { get; set; }
    public string? InstalledSize { get; set; }
    public string? Depends { get; set; }
    public string? Recommends { get; set; }
    public string? Suggests { get; set; }
    public string? Conflicts { get; set; }
    public string? Breaks { get; set; }
    public string? Replaces { get; set; }
    public string? Provides { get; set; }
    public string? Source { get; set; }
    public string? MultiArch { get; set; }
    public string? OriginalMaintainer { get; set; }

    // ── File storage metadata ──

    /// <summary>
    /// Debian-style pool path: <c>pool/{component}/{pkg[0]}/{pkg}/{pkg}_{ver}_{arch}.deb</c>.
    /// </summary>
    [Required]
    public required string Filename { get; set; }

    /// <summary>
    /// File size in bytes, stored as string for APT protocol compatibility.
    /// </summary>
    [Required]
    public required string Size { get; set; }

    /// <summary>
    /// SHA-256 of the .deb file. Computed server-side on upload (authoritative).
    /// </summary>
    [Required]
    [MaxLength(64)]
    public required string SHA256 { get; set; }

    [MaxLength(32)]
    public string? MD5sum { get; set; }

    [MaxLength(40)]
    public string? SHA1 { get; set; }

    [MaxLength(128)]
    public string? SHA512 { get; set; }
}
