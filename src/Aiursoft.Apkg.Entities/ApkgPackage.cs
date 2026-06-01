using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
[Index(nameof(Name), nameof(Distro), nameof(Component), IsUnique = true)]
public class ApkgPackage
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(128)]
    public required string Name { get; set; }

    [Required]
    [MaxLength(100)]
    public string Distro { get; set; } = "ubuntu";

    [Required]
    [MaxLength(128)]
    public required string Component { get; set; }

    public string? Description { get; set; }

    public string? Maintainer { get; set; }

    [MaxLength(512)]
    public string? Homepage { get; set; }

    [MaxLength(256)]
    public string? License { get; set; }

    [Required]
    public required string OwnerUserId { get; set; }

    [ForeignKey(nameof(OwnerUserId))]
    public User? OwnerUser { get; set; }

    public ICollection<ApkgRevision> Revisions { get; set; } = [];
}
