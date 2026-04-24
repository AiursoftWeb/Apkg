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

    [Required]
    [MaxLength(255)]
    public string Components { get; set; } = "main";

    [Required]
    [MaxLength(100)]
    public string Architecture { get; set; } = "amd64";

    public int? CertificateId { get; set; }

    [ForeignKey(nameof(CertificateId))]
    public AptCertificate? Certificate { get; set; }

    public bool EnableGpgSign { get; set; } = true;

    public int? MirrorId { get; set; }

    [ForeignKey(nameof(MirrorId))]
    public AptMirror? Mirror { get; set; }

    public int? PrimaryBucketId { get; set; }

    [ForeignKey(nameof(PrimaryBucketId))]
    public AptBucket? PrimaryBucket { get; set; }

    /// <summary>
    /// The bucket that has been built and is awaiting GPG signing before being promoted to <see cref="PrimaryBucketId"/>.
    /// Set by <c>RepositorySyncJob</c>; cleared and promoted to <see cref="PrimaryBucketId"/> by <c>RepositorySignJob</c>.
    /// </summary>
    public int? SecondaryBucketId { get; set; }

    [ForeignKey(nameof(SecondaryBucketId))]
    public AptBucket? SecondaryBucket { get; set; }
}
