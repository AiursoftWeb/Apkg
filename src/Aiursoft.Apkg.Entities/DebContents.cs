using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

/// <summary>
/// <para><b>DebContents</b> — A cache of <c>dpkg-deb -c</c> output, keyed by SHA256.</para>
///
/// <para><b>Design purpose:</b> <c>dpkg-deb -c</c> decompresses the entire data.tar
/// inside a .deb file to list its file paths. For a large package (e.g. 2.8 GB
/// anduinos-why-ai), this is extremely I/O-intensive. Since .deb files are immutable
/// in Content-Addressable Storage (CAS), the file listing for a given SHA256 NEVER
/// changes — compute once, reuse forever.</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Populated eagerly by <c>DebUploadService</c> at upload time.</item>
///   <item>Populated lazily by <c>RepositorySyncJob</c> during Contents generation
///   on cache miss (covers mirror-synced packages).</item>
///   <item>Read by <c>ContentsGeneratorService</c> via <c>DebContentsService</c>
///   to skip <c>dpkg-deb -c</c> during sync.</item>
///   <item><c>ContentsCacheBackfillJob</c> bulk-populates for existing packages.</item>
/// </list>
///
/// <para><b>Normal form compliance:</b></para>
/// <list type="bullet">
///   <item><b>1NF ✅</b> — Single row per SHA256, ContentsJson is atomic (serialized array).</item>
///   <item><b>2NF ✅</b> — SHA256 is the sole PK. ContentsJson depends on the full PK.</item>
///   <item><b>3NF ✅</b> — No transitive dependencies. ContentsJson is derived from the
///   .deb binary identified by SHA256, not from other columns.</item>
/// </list>
/// </summary>
[ExcludeFromCodeCoverage]
[Index(nameof(CreatedAt))]
public class DebContents
{
    /// <summary>
    /// SHA-256 hex digest of the .deb file. This is the authoritative content-addressable
    /// key matching the CAS path <c>Objects/{sha256[..2]}/{sha256}.deb</c>.
    /// </summary>
    [Key]
    [MaxLength(64)]
    public required string SHA256 { get; set; }

    /// <summary>
    /// JSON-serialized array of file paths from <c>dpkg-deb -c</c> output, pre-parsed
    /// via <see cref="Aiursoft.Apkg.Services.Contents.ContentsGeneratorService.ParseDpkgDebContents"/>.
    /// Example: <c>["usr/bin/myapp","etc/myapp.conf","usr/share/doc/myapp/readme"]</c>
    /// </summary>
    [Required]
    public required string ContentsJson { get; set; }

    /// <summary>
    /// When this cache entry was first computed.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this cache entry was last updated (recomputed or refreshed).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
