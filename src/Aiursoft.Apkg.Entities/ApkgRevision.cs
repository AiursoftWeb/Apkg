using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>ApkgRevision</b> — Records a single <c>apkg push</c> event.</para>
///
/// <para><b>Design purpose:</b> Each push uploads one .apkg archive containing a
/// manifest.xml plus N .deb files for different Suite × Architecture combinations.
/// This row tracks the push: who uploaded it, the .apkg file, and links to the
/// resulting <see cref="ApkgDebPackage"/> records.</para>
///
/// <para><b>Lifecycle:</b></para>
/// <list type="number">
///   <item><b>Uploaded</b>: .apkg file stored in vault, TempApkgFileInVaultPath set.</item>
///   <item><b>Published</b>: .deb files extracted and uploaded to repos,
///   TempApkgFileInVaultPath cleared (source file consumed).</item>
///   <item><b>Abandoned</b>: User never clicked Publish →
///   <c>ApkgTempCleanupJob</c> deletes revisions with
///   TempApkgFileInVaultPath != null older than 30 minutes.</item>
/// </list>
///
/// <para><b>Published detection:</b> A revision is considered "published" when
/// <c>TempApkgFileInVaultPath == null</c> — the source .apkg has been consumed.
/// No separate boolean flag is needed.</para>
///
/// <para><b>Relationships:</b></para>
/// <list type="bullet">
///   <item>Belongs to <see cref="ApkgPackage"/> via ApkgPackageId (many-to-one,
///   cascade delete).</item>
///   <item>Has <see cref="ApkgDebPackage"/>s (one-to-many, cascade delete).</item>
///   <item>Has uploader <see cref="User"/> via UploadedByUserId.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
public class ApkgRevision
{
    [Key]
    public int Id { get; set; }

    public int ApkgPackageId { get; set; }

    [ForeignKey(nameof(ApkgPackageId))]
    public ApkgPackage? ApkgPackage { get; set; }

    [Required]
    public required string UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedByUser { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Original .apkg filename from the client.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public required string FileName { get; set; }

    /// <summary>
    /// Temporary path to the uploaded .apkg archive in the vault.
    /// Null means the .apkg has been consumed (published) and the file deleted.
    /// Non-null means the .apkg file is awaiting Publish (or will be cleaned up by ApkgTempCleanupJob).
    /// </summary>
    [MaxLength(512)]
    public string? TempApkgFileInVaultPath { get; set; }

    /// <summary>
    /// True if this revision is listed in the Web UI. Can be toggled by
    /// admin (Unlist/Relist actions) without deleting data.
    /// </summary>
    public bool IsListed { get; set; } = true;

    public ICollection<ApkgDebPackage> ApkgDebPackages { get; set; } = [];
    public ICollection<ApkgAppStreamApplication> AppStreamApplications { get; set; } = [];
}
