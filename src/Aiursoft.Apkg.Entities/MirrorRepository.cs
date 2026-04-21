using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
public class MirrorRepository
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public required string BaseUrl { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Suite { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Component { get; set; }

    [Required]
    [MaxLength(20)]
    public required string Architecture { get; set; }

    public string? SignedBy { get; set; }
}
