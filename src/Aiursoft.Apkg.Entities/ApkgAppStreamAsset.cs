using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

[Index(nameof(ObjectSha256))]
[Index(nameof(ApkgAppStreamApplicationId), nameof(Order), IsUnique = true)]
public sealed class ApkgAppStreamAsset
{
    [Key]
    public int Id { get; set; }

    public int ApkgAppStreamApplicationId { get; set; }

    [ForeignKey(nameof(ApkgAppStreamApplicationId))]
    public ApkgAppStreamApplication? ApkgAppStreamApplication { get; set; }

    [Required]
    [MaxLength(64)]
    public required string SourceSha256 { get; set; }

    [Required]
    [MaxLength(64)]
    public required string ObjectSha256 { get; set; }

    [Required]
    [MaxLength(64)]
    public required string MediaType { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsDefault { get; set; }
    public int Order { get; set; }

    [Required]
    [MaxLength(35)]
    public required string Locale { get; set; }

    [MaxLength(128)]
    public string? Environment { get; set; }

    [MaxLength(512)]
    public string? Caption { get; set; }
}
