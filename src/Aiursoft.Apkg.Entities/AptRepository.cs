using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
public class AptRepository
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Distro { get; set; } = "ubuntu";

    [Required]
    [MaxLength(100)]
    public required string Name { get; set; } // The name used in the URL (e.g. "ubuntu")

    [Required]
    [MaxLength(100)]
    public required string Suite { get; set; } // The suite name (e.g. "questing")

    public int? CertificateId { get; set; }

    [ForeignKey(nameof(CertificateId))]
    public AptCertificate? Certificate { get; set; }

    public int? MirrorId { get; set; }

    [ForeignKey(nameof(MirrorId))]
    public AptMirror? Mirror { get; set; }

    public int? CurrentBucketId { get; set; }

    [ForeignKey(nameof(CurrentBucketId))]
    public AptBucket? CurrentBucket { get; set; }
}
