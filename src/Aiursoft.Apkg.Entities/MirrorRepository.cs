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
    [MaxLength(255)]
    public required string Components { get; set; } // Comma separated: main,restricted,universe,multiverse

    public string? SignedBy { get; set; }
}
