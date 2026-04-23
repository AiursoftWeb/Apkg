using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
public class AptBucket
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set to true once the sync job has successfully finished building this bucket.
    /// GC will only delete orphaned buckets where this is true, eliminating the need
    /// for a time-based grace period.
    /// </summary>
    public bool BuildFinished { get; set; }

    // For Repository buckets, stores the pre-signed metadata
    public string? InReleaseContent { get; set; }
    public string? ReleaseContent { get; set; }

    /// <summary>
    /// Set by <c>RepositorySignJob</c> when the bucket's Release file is GPG-signed.
    /// Null means the bucket has not been signed yet.
    /// </summary>
    public DateTime? SignedAt { get; set; }

    public List<AptPackage> Packages { get; set; } = [];
}
