using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
[Index(nameof(RepositoryId), nameof(Package), nameof(Architecture))]
[Index(nameof(UploadedByUserId))]
public class LocalPackage
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedByUser { get; set; }

    public int RepositoryId { get; set; }

    [ForeignKey(nameof(RepositoryId))]
    public AptRepository? Repository { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsEnabled { get; set; } = true;

    [Required]
    [MaxLength(100)]
    public required string Component { get; set; }

    // APT metadata (extracted from .deb control file)
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

    // File storage (CAS: Objects/{sha256[0..1]}/{sha256}.deb)
    [Required]
    public required string Filename { get; set; }  // pool/{component}/{pkg[0]}/{pkg}/{pkg}_{ver}_{arch}.deb

    [Required]
    public required string Size { get; set; }  // bytes as string

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
