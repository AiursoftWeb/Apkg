using System.Security.Claims;
using System.Text.Json;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Models;
using Aiursoft.Apkg.Services;
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
    ChunkedUploadService uploads,
    ILogger<ChunkedUploadController> logger) : ControllerBase
{
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

        await uploads.CreateAsync(session);

        logger.LogInformation(
            "Chunked upload session {SessionId} initialized by user {UserId}: {FileName} ({TotalSize} bytes, {ChunkCount} chunks)",
            sessionId, userId, request.FileName, request.TotalSize, request.ChunkCount);

        return Ok(new
        {
            sessionId,
            backgroundCompletion = true,
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
        var session = await uploads.LoadAsync(sessionId);
        if (session == null)
            return NotFound(new { error = $"Upload session '{sessionId}' not found." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (session.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "You are not the owner of this upload session." });

        if (session.Completion != null)
            return Conflict(new { error = "Upload has already been submitted for processing." });

        if (chunkIndex < 0 || chunkIndex >= session.ChunkCount)
            return BadRequest(new { error = $"Chunk index {chunkIndex} is out of range [0, {session.ChunkCount})." });

        var sessionDir = uploads.GetSessionDirectory(sessionId);
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

            if (!await uploads.CommitChunkAsync(sessionId, tempPath, chunkIndex))
                return Conflict(new { error = "Upload has already been submitted for processing." });
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
            Request.ContentLength);

        return Ok(new { chunkIndex, received = true });
    }

    /// <summary>
    /// Queues merge, verification and import. New clients request background=true
    /// and poll status; older clients retain their synchronous summary response.
    /// </summary>
    [HttpPost("{sessionId}/complete")]
    public async Task<IActionResult> Complete(string sessionId, bool background = false)
    {
        var session = await uploads.LoadAsync(sessionId);
        if (session == null)
            return NotFound(new { error = $"Upload session '{sessionId}' not found." });
        if (session.UserId != User.FindFirstValue(ClaimTypes.NameIdentifier))
            return StatusCode(403, new { error = "You are not the owner of this upload session." });

        try
        {
            var status = await uploads.QueueAsync(sessionId, User);
            if (background)
            {
                Response.Headers.Location = Url.Action(nameof(Status), new { sessionId });
                return StatusResponse(status, 202);
            }
            while (status.Status is "Queued" or "Processing")
            {
                await Task.Delay(100, HttpContext.RequestAborted);
                status = (await uploads.LoadAsync(sessionId))!.Completion!;
            }
            return new ContentResult
            {
                StatusCode = status.StatusCode,
                ContentType = "application/json",
                Content = status.Result?.GetRawText()
            };
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{sessionId}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status(string sessionId)
    {
        var session = await uploads.LoadAsync(sessionId);
        if (session == null)
            return NotFound(new { error = $"Upload session '{sessionId}' not found." });
        if (session.UserId != User.FindFirstValue(ClaimTypes.NameIdentifier))
            return StatusCode(403, new { error = "You are not the owner of this upload session." });
        return StatusResponse(session.Completion ?? new ChunkedUploadStatus { Status = "Uploading" }, 200);
    }

    private static ContentResult StatusResponse(ChunkedUploadStatus status, int statusCode) => new()
    {
        StatusCode = statusCode,
        ContentType = "application/json",
        Content = JsonSerializer.Serialize(status, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };
}
