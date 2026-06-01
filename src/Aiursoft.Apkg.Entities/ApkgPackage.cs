using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>ApkgPackage</b> — Immutable package family identified by the "three dead attributes":
/// (Name, Distro, Component).</para>
///
/// <para><b>Design purpose:</b> This is the platform-level package identity — distinct from
/// <see cref="AptPackage"/> (low-level APT metadata) and <see cref="ApkgDebPackage"/> (user override).
/// Represents one "project" on Apkg. The triplet uniquely identifies a package family; once set,
/// it can never change (see docs/design.md §11, aosproj.md §2).</para>
///
/// <para><b>"Three dead + three alive" model (see aosproj.md §2):</b></para>
/// <list type="bullet">
///   <item><b>Dead</b> (immutable at this level): Name, Distro, Component — the triplet identity.</item>
///   <item><b>Alive</b> (vary per push, at Revision/Entry level): Version, Suite, Architecture.</item>
/// </list>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Created on first <c>apkg push</c> for a given (Name, Distro, Component).
///   The pushing user becomes the Owner.</item>
///   <item>Subsequent pushes add new <see cref="ApkgRevision"/>s under this package.</item>
///   <item>If a different user pushes the same triplet → 403 Forbidden (ownership).</item>
///   <item>Web UI shows package details, revision history, and associated ApkgDebPackages.</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Created</b> on first push for a triplet. OwnerUserId set to pushing user.</item>
///   <item><b>Accumulates revisions</b> with each subsequent push.</item>
///   <item><b>Never deleted</b> in normal operation — even if all revisions are
///   cleaned up, the ApkgPackage row persists to preserve ownership.</item>
/// </list>
///
/// <para><b>Relationships:</b></para>
/// <list type="bullet">
///   <item>Owns <see cref="ApkgRevision"/>s (one-to-many, cascade delete:
///   deleting a package removes all its revisions and their ApkgDebPackages).</item>
///   <item>Has owner <see cref="User"/> via OwnerUserId.</item>
/// </list>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — All columns atomic.</item>
///   <item><b>2NF ✅</b> — PK is Id. Unique index on (Name, Distro, Component)
///   prevents duplicate package families.</item>
///   <item><b>3NF ✅</b> — Description, Maintainer, Homepage, License are metadata
///   describing the package family — all depend on the PK, not on each other.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
[Index(nameof(Name), nameof(Distro), nameof(Component), IsUnique = true)]
public class ApkgPackage
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Package name. Dead attribute — part of the immutable triplet.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public required string Name { get; set; }

    /// <summary>
    /// Target distro (e.g., "anduinos", "ubuntu"). Dead attribute.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Distro { get; set; } = "ubuntu";

    /// <summary>
    /// APT component (e.g., "main", "community"). Dead attribute.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public required string Component { get; set; }

    /// <summary>
    /// Package description from manifest.xml.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Package maintainer from manifest.xml.
    /// </summary>
    public string? Maintainer { get; set; }

    [MaxLength(512)]
    public string? Homepage { get; set; }

    [MaxLength(256)]
    public string? License { get; set; }

    /// <summary>
    /// The user who first pushed this (Name, Distro, Component) triplet.
    /// Only this user can push subsequent revisions.
    /// </summary>
    [Required]
    public required string OwnerUserId { get; set; }

    [ForeignKey(nameof(OwnerUserId))]
    public User? OwnerUser { get; set; }

    /// <summary>
    /// When this package family was first created. Used by <c>ApkgOrphanPackageCleanupJob</c>
    /// to delete packages with 0 revisions older than 2 hours.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ApkgRevision> Revisions { get; set; } = [];
}
