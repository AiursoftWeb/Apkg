using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

[Index(nameof(ApkgRevisionId), nameof(ComponentId), IsUnique = true)]
public sealed class ApkgAppStreamApplication
{
    [Key]
    public int Id { get; set; }

    public int ApkgRevisionId { get; set; }

    [ForeignKey(nameof(ApkgRevisionId))]
    public ApkgRevision? ApkgRevision { get; set; }

    [Required]
    [MaxLength(255)]
    public required string ComponentId { get; set; }

    [Required]
    [MaxLength(255)]
    public required string DesktopId { get; set; }

    [Required]
    [MaxLength(512)]
    public required string MetainfoPath { get; set; }

    public ICollection<ApkgAppStreamAsset> Assets { get; set; } = [];
}
