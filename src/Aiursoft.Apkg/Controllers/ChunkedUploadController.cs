using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aiursoft.Apkg.Models;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Apkg.Controllers;

/// <summary>
/// Provides a chunked upload API for large .apkg files.
/// This allows bypassing CDN/reverse-proxy body-size limits (e.g. Cloudflare 100 MB)
/// by splitting the file into multiple smaller chunks.
///
/// Flow: Init → Upload Chunks → Complete
/// </summary>
[ApiController]
[Route("api/upload")]
[Authorize(AuthenticationSchemes = "ApiKey,Identity.Application")]
public class ChunkedUploadController(
    ApkgUploadProcessor uploadProcessor,
    FeatureFoldersProvider folders,
    ILogger<ChunkedUploadController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Phase 1: Initialize a chunked upload session.
    /// Returns a session ID that the client uses for subsequent chunk uploads.
    /// </summary>
    [HttpPost("init")]
    public async Task<IActionResult> Init([FromBody] ChunkedUploadInitRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionDir = GetSessionDirectory(sessionId);

        Directory.CreateDirectory(sessionDir);

        var session = new ChunkedUploadSession
        {
            SessionId = sessionId,
            FileName = request.FileName,
            TotalSize = request.TotalSize,
            ChunkCount = request.ChunkCount,
            FileHash = request.FileHash.ToLowerInvariant(),
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            SkipDuplicate = request.SkipDuplicate,
            AllowDowngrade = request.AllowDowngrade
        };

        var sessionJsonPath = Path.Combine(sessionDir, "session.json");
        await System.IO.File.WriteAllTextAsync(sessionJsonPath,
            JsonSerializer.Serialize(session, JsonOptions));

        logger.LogInformation(
            "Chunked upload session {SessionId} initialized by user {UserId}: {FileName} ({TotalSize} bytes, {ChunkCount} chunks)",
            sessionId, userId, request.FileName, request.TotalSize, request.ChunkCount);

        return Ok(new
        {
            sessionId,
            chunkSize = request.TotalSize / request.ChunkCount
        });
    }

    /// <summary>
    /// Phase 2: Upload a single chunk.
    /// The request body is the raw binary chunk data.
    /// </summary>
    [HttpPut("{sessionId}/chunks/{chunkIndex:int}")]
    [RequestSizeLimit(200L * 1024 * 1024)] // 200 MB hard cap per chunk to prevent abuse
    public async Task<IActionResult> UploadChunk(string sessionId, int chunkIndex)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            return NotFound(new { error = $"Upload session '{sessionId}' not found." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (session.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "You are not the owner of this upload session." });

        if (chunkIndex < 0 || chunkIndex >= session.ChunkCount)
            return BadRequest(new { error = $"Chunk index {chunkIndex} is out of range [0, {session.ChunkCount})." });

        var sessionDir = GetSessionDirectory(sessionId);
        var chunkPath = Path.Combine(sessionDir, $"chunk_{chunkIndex}");

        // Write to a temp file first, then atomically move to the final location.
        // This prevents "file being used by another process" errors when the client
        // retries a chunk upload while a previous attempt is still writing.
        var tempPath = chunkPath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            await using (var chunkFile = System.IO.File.Create(tempPath))
            {
                await Request.Body.CopyToAsync(chunkFile);
            }

            System.IO.File.Move(tempPath, chunkPath, overwrite: true);
        }
        finally
        {
            // Clean up temp file in case Move failed or an exception was thrown
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }

        logger.LogInformation(
            "Chunked upload session {SessionId}: chunk {ChunkIndex}/{ChunkCount} received ({Size} bytes)",
            sessionId, chunkIndex, session.ChunkCount,
            new FileInfo(chunkPath).Length);

        return Ok(new { chunkIndex, received = true });
    }

    /// <summary>
    /// Phase 3: Complete the upload — merge chunks, verify hash, process the .apkg.
    /// </summary>
    [HttpPost("{sessionId}/complete")]
    public async Task<IActionResult> Complete(string sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null)
            return NotFound(new { error = $"Upload session '{sessionId}' not found." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (session.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "You are not the owner of this upload session." });

        var sessionDir = GetSessionDirectory(sessionId);

        // Verify all chunks are present
        var missingChunks = new List<int>();
        for (var i = 0; i < session.ChunkCount; i++)
        {
            if (!System.IO.File.Exists(Path.Combine(sessionDir, $"chunk_{i}")))
                missingChunks.Add(i);
        }

        if (missingChunks.Count > 0)
            return BadRequest(new
            {
                error = $"Missing {missingChunks.Count} chunk(s): [{string.Join(", ", missingChunks)}]"
            });

        // Merge chunks into a single file
        var mergedFilePath = Path.Combine(folders.GetWorkspaceFolder(),
            $"chunked-merge-{Guid.NewGuid()}.apkg");
        try
        {
            using var sha256Hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var mergedStream = System.IO.File.Create(mergedFilePath))
            {
                for (var i = 0; i < session.ChunkCount; i++)
                {
                    var chunkPath = Path.Combine(sessionDir, $"chunk_{i}");
                    await using var chunkStream = System.IO.File.OpenRead(chunkPath);
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await chunkStream.ReadAsync(buffer)) > 0)
                    {
                        sha256Hasher.AppendData(buffer, 0, read);
                        await mergedStream.WriteAsync(buffer.AsMemory(0, read));
                    }
                }
            }

            // Verify hash
            var actualHash = Convert.ToHexStringLower(sha256Hasher.GetHashAndReset());
            if (!string.Equals(actualHash, session.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    error = $"Hash mismatch. Expected: {session.FileHash}, Actual: {actualHash}"
                });
            }

            logger.LogInformation(
                "Chunked upload session {SessionId}: all {ChunkCount} chunks merged, hash verified. Processing .apkg...",
                sessionId, session.ChunkCount);

            // Process the merged .apkg file using the shared processor
            var result = await uploadProcessor.ProcessApkgFileAsync(
                mergedFilePath,
                session.FileName,
                userId,
                User,
                session.SkipDuplicate,
                session.AllowDowngrade);

            return StatusCode(result.StatusCode, result.Summary);
        }
        finally
        {
            // Clean up merged file
            if (System.IO.File.Exists(mergedFilePath))
                System.IO.File.Delete(mergedFilePath);

            // Clean up session directory
            try
            {
                if (Directory.Exists(sessionDir))
                    Directory.Delete(sessionDir, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up chunked upload session directory: {SessionDir}", sessionDir);
            }
        }
    }

    private string GetSessionDirectory(string sessionId)
    {
        return Path.Combine(folders.GetChunkedUploadsFolder(), sessionId);
    }

    private async Task<ChunkedUploadSession?> LoadSessionAsync(string sessionId)
    {
        // Sanitize sessionId to prevent path traversal
        if (sessionId.Contains('.') || sessionId.Contains('/') || sessionId.Contains('\\'))
            return null;

        var sessionJsonPath = Path.Combine(GetSessionDirectory(sessionId), "session.json");
        if (!System.IO.File.Exists(sessionJsonPath))
            return null;

        var json = await System.IO.File.ReadAllTextAsync(sessionJsonPath);
        return JsonSerializer.Deserialize<ChunkedUploadSession>(json, JsonOptions);
    }
}
