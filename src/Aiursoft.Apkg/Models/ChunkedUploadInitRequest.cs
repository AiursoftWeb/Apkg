using System.ComponentModel.DataAnnotations;

namespace Aiursoft.Apkg.Models;

/// <summary>
/// Request body for POST /api/upload/init.
/// </summary>
public class ChunkedUploadInitRequest
{
    [Required]
    [MaxLength(256)]
    public required string FileName { get; init; }
    
    [Range(1, long.MaxValue)]
    public required long TotalSize { get; init; }
    
    [Range(1, 10000)]
    public required int ChunkCount { get; init; }
    
    /// <summary>
    /// SHA-256 hash (lowercase hex, 64 characters) of the complete file.
    /// </summary>
    [Required]
    [StringLength(64, MinimumLength = 64)]
    [RegularExpression("^[0-9a-f]{64}$", ErrorMessage = "FileHash must be a lowercase hex SHA-256 string (64 characters).")]
    public required string FileHash { get; init; }
    
    public bool SkipDuplicate { get; init; }
    public bool AllowDowngrade { get; init; }
}
