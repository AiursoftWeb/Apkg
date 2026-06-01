using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

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

    [Required]
    [MaxLength(256)]
    public required string FileName { get; set; }

    [MaxLength(512)]
    public string? VaultPath { get; set; }

    public bool IsPublished { get; set; }

    public bool IsListed { get; set; } = true;

    public ICollection<LocalPackage> LocalPackages { get; set; } = [];
}
