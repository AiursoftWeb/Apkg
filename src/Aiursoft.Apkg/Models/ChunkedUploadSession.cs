namespace Aiursoft.Apkg.Models;

/// <summary>
/// Persisted to disk as session.json inside each chunked upload session folder.
/// Tracks the state of an in-progress chunked upload.
/// </summary>
public class ChunkedUploadSession
{
    public required string SessionId { get; init; }
    public required string FileName { get; init; }
    public required long TotalSize { get; init; }
    public required int ChunkCount { get; init; }
    
    /// <summary>
    /// Expected SHA-256 hash (lowercase hex) of the final assembled file.
    /// </summary>
    public required string FileHash { get; init; }
    
    public required string UserId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    
    public bool SkipDuplicate { get; init; }
    public bool AllowDowngrade { get; init; }
}
