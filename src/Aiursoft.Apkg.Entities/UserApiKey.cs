using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
[Index(nameof(KeyHash), IsUnique = true)]
[Index(nameof(UserId))]
public class UserApiKey
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>
    /// A human-readable label for this key, e.g. "CI/CD Pipeline" or "Dev Machine".
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string Name { get; set; }

    /// <summary>
    /// SHA-256 hex digest of the raw API key. The raw key is shown once at creation and never stored.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public required string KeyHash { get; set; }

    /// <summary>
    /// First 8 characters of the raw key, kept for display purposes (e.g. "ab12cd34...").
    /// Safe to store — too short to reconstruct the full key.
    /// </summary>
    [Required]
    [MaxLength(8)]
    public required string KeyPrefix { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// When this key expires. Null means it never expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}
