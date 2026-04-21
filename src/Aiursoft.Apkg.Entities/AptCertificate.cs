using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
public class AptCertificate
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public required string FriendlyName { get; set; }

    [Required]
    public required string PublicKey { get; set; }

    [Required]
    public required string PrivateKey { get; set; }

    [Required]
    [MaxLength(50)]
    public required string Fingerprint { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
