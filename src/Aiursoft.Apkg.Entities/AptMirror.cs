using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
public class AptMirror
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Distro { get; set; } = "ubuntu";

    [Required]
    [MaxLength(255)]
    public required string BaseUrl { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Suite { get; set; }

    [Required]
    [MaxLength(255)]
    public required string Components { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Architecture { get; set; }

    public string? SignedBy { get; set; }

    public int? PrimaryBucketId { get; set; }

    [ForeignKey(nameof(PrimaryBucketId))]
    public AptBucket? PrimaryBucket { get; set; }

    public int? SecondaryBucketId { get; set; }

    [ForeignKey(nameof(SecondaryBucketId))]
    public AptBucket? SecondaryBucket { get; set; }

    public DateTime? LastPullTime { get; set; }

    public bool? LastPullSuccess { get; set; }

    public string? LastPullResult { get; set; }

    public string? LastPullErrorStack { get; set; }
}
