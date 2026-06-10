using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.AptClient.Abstractions;

/// <summary>
/// <para><b>DebianPackage</b> — Base class representing a single package record from a
/// Debian/APT Packages.gz index file. Contains all standard APT metadata fields.</para>
///
/// <para><b>Design purpose:</b> This is the canonical C# representation of a single
/// paragraph in a Debian Packages file. It is inherited by <c>AptPackage</c> (the EF Core
/// entity stored in the database) so that buckets can hold structured, queryable package
/// records rather than raw text.</para>
///
/// <para><b>Usage:</b></para>
/// <list type="bullet">
///   <item>Inherited by <c>AptPackage</c> for database storage.</item>
///   <item>Also used as a DTO by the AptClient for parsing upstream Packages.gz files.</item>
/// </list>
///
/// <para><b>Normal form compliance:</b> This is not a database entity — it's a base class.
/// When persisted via <c>AptPackage</c>, all fields are atomic (1NF). The <c>Extras</c>
/// dictionary is serialized to JSON string for storage to handle the "long tail" of
/// rarely-used APT fields.</para>
/// </summary>
[ExcludeFromCodeCoverage]
public class DebianPackage
{
    // ==========================================
    // 1. Required properties (present in 100% of packages)
    // ==========================================

    /// <summary>
    /// The suite this package was ingested from (e.g., "noble", "noble-updates").
    /// </summary>
    [MaxLength(128)]
    public required string OriginSuite { get; set; }

    /// <summary>
    /// The component this package was ingested from (e.g., "main", "restricted").
    /// </summary>
    [MaxLength(128)]
    public required string OriginComponent { get; set; }

    /// <summary>
    /// Package name (e.g., "chromium-browser").
    /// </summary>
    [MaxLength(128)]
    public required string Package { get; set; }

    /// <summary>
    /// Debian version string (e.g., "1:126.0.6478.55-1~deb12u1").
    /// </summary>
    [MaxLength(128)]
    public required string Version { get; set; }

    /// <summary>
    /// CPU architecture (e.g., "amd64", "arm64", "i386", "all").
    /// </summary>
    [MaxLength(128)]
    public required string Architecture { get; set; }

    /// <summary>
    /// Maintainer field from the .deb control file.
    /// </summary>
    public required string Maintainer { get; set; }

    /// <summary>
    /// Single-line package description.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// MD5 hash of the long Description (for APT translation support).
    /// </summary>
    public string DescriptionMd5 { get; set; } = string.Empty;

    /// <summary>
    /// Section classification (e.g., "utils", "editors", "libs").
    /// </summary>
    public required string Section { get; set; }

    /// <summary>
    /// Priority (e.g., "required", "important", "standard", "optional", "extra").
    /// </summary>
    public required string Priority { get; set; }

    /// <summary>
    /// Origin label (e.g., "Ubuntu").
    /// </summary>
    public required string Origin { get; set; }

    /// <summary>
    /// Bug tracking URL.
    /// </summary>
    public required string Bugs { get; set; }

    /// <summary>
    /// Pool path relative to the repo base (e.g., "pool/main/c/chromium-browser/chromium-browser_126_amd64.deb").
    /// </summary>
    public required string Filename { get; set; }

    /// <summary>
    /// File size in bytes, stored as string for APT protocol compatibility.
    /// </summary>
    public required string Size { get; set; }

    /// <summary>
    /// MD5 checksum of the .deb file.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public required string MD5sum { get; set; }

    /// <summary>
    /// SHA-1 checksum of the .deb file.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public required string SHA1 { get; set; }

    /// <summary>
    /// SHA-256 checksum of the .deb file. Primary key for CAS storage.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public required string SHA256 { get; set; }

    /// <summary>
    /// SHA-512 checksum of the .deb file.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public required string SHA512 { get; set; }

    // ==========================================
    // 2. Common optional properties (high coverage)
    // ==========================================

    public string? InstalledSize { get; set; }      // 99.9%
    public string? OriginalMaintainer { get; set; } // 94.5%
    public string? Homepage { get; set; }           // 92.2%
    public string? Depends { get; set; }            // 87.7%
    public string? Source { get; set; }             // 69.5%
    public string? MultiArch { get; set; }          // 38.4%

    public string? Provides { get; set; }
    public string? Suggests { get; set; }
    public string? Recommends { get; set; }
    public string? Conflicts { get; set; }
    public string? Breaks { get; set; }
    public string? Replaces { get; set; }

    // ==========================================
    // 3. Long-tail catch-all
    // ==========================================

    /// <summary>
    /// Catch-all for fields not explicitly modeled (e.g., Ruby-Versions,
    /// Python-Egg-Name, etc.). Stored as JSON string in the database via
    /// a custom EF Core ValueConverter and ValueComparer.
    /// </summary>
    public Dictionary<string, string> Extras { get; set; } = new();
}
