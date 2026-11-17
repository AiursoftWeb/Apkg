using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aiursoft.Apkg.Models;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Canon.TaskQueue;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Serializes short session updates. Large-file processing runs outside the lock
/// in the existing task queue, with its result retained in session.json.
/// </summary>
public sealed class ChunkedUploadService(
    FeatureFoldersProvider folders,
    ServiceTaskQueue taskQueue,
    ILogger<ChunkedUploadService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Guid _instanceId = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetSessionDirectory(string sessionId) =>
        Path.Combine(folders.GetChunkedUploadsFolder(), sessionId);

    public async Task CreateAsync(ChunkedUploadSession session)
    {
        Directory.CreateDirectory(GetSessionDirectory(session.SessionId));
        await SaveAsync(session);
    }

    public async Task<ChunkedUploadSession?> LoadAsync(string sessionId)
    {
        await _gate.WaitAsync();
        try
        {
            return await LoadCoreAsync(sessionId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> CommitChunkAsync(string sessionId, string tempPath, int chunkIndex)
    {
        await _gate.WaitAsync();
        try
        {
            var session = await LoadCoreAsync(sessionId);
            if (session == null || session.Completion != null)
                return false;
            File.Move(tempPath, Path.Combine(GetSessionDirectory(sessionId), $"chunk_{chunkIndex}"), overwrite: true);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChunkedUploadStatus> QueueAsync(string sessionId, ClaimsPrincipal user)
    {
        await _gate.WaitAsync();
        try
        {
            var session = await LoadCoreAsync(sessionId)
                ?? throw new InvalidDataException("Upload session no longer exists.");
            if (session.Completion != null)
                return session.Completion;

            var missing = Enumerable.Range(0, session.ChunkCount)
                .Where(i => !File.Exists(Path.Combine(GetSessionDirectory(sessionId), $"chunk_{i}")))
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"Missing {missing.Length} chunk(s): [{string.Join(", ", missing)}]");

            session.Completion = new ChunkedUploadStatus { Status = "Queued" };
            session.ProcessingInstanceId = _instanceId;
            // Persist acceptance before enqueuing. A restart in either step is
            // reported as interrupted, never as successful or silently replayed.
            await SaveAsync(session);
            var principal = user.Clone();
            session.JobId = taskQueue.QueueWithDependency<ApkgUploadProcessor>(
                "Chunked uploads", $"Process upload {sessionId}",
                processor => ProcessAsync(sessionId, principal, processor));
            await SaveAsync(session);
            return session.Completion;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ProcessAsync(string sessionId, ClaimsPrincipal user, ApkgUploadProcessor processor)
    {
        ChunkedUploadSession session;
        await _gate.WaitAsync();
        try
        {
            session = (await LoadCoreAsync(sessionId))!;
            if (session.Completion?.Status != "Queued")
                return;
            session.Completion.Status = "Processing";
            await SaveAsync(session);
        }
        finally
        {
            _gate.Release();
        }

        ChunkedUploadStatus completion;
        try
        {
            completion = await MergeAndProcessAsync(session, user, processor);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chunked upload {SessionId} failed", sessionId);
            completion = Failure("Upload processing failed. Check the server logs before retrying.");
        }

        // Release large files, keeping the small receipt available for 24 hours.
        CleanupFiles(sessionId);
        await _gate.WaitAsync();
        try
        {
            session.Completion = completion;
            await SaveAsync(session);
        }
        finally
        {
            _gate.Release();
        }
        if (completion.Status == "Failed")
            throw new InvalidOperationException($"Upload {sessionId} failed: {completion.Result}");
    }

    private async Task<ChunkedUploadStatus> MergeAndProcessAsync(
        ChunkedUploadSession session, ClaimsPrincipal user, ApkgUploadProcessor processor)
    {
        var sessionDir = GetSessionDirectory(session.SessionId);
        var mergedPath = Path.Combine(sessionDir, "merged.apkg");
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long totalSize = 0;
        await using (var merged = File.Create(mergedPath))
        {
            var buffer = new byte[81920];
            for (var i = 0; i < session.ChunkCount; i++)
            {
                await using var chunk = File.OpenRead(Path.Combine(sessionDir, $"chunk_{i}"));
                int read;
                while ((read = await chunk.ReadAsync(buffer)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await merged.WriteAsync(buffer.AsMemory(0, read));
                    totalSize += read;
                }
            }
        }
        var actualHash = Convert.ToHexStringLower(hasher.GetHashAndReset());
        if (actualHash != session.FileHash)
            return Failure($"Hash mismatch. Expected: {session.FileHash}, Actual: {actualHash}", 400);
        if (totalSize != session.TotalSize)
            return Failure($"Size mismatch. Expected: {session.TotalSize}, Actual: {totalSize}", 400);

        logger.LogInformation("Chunked upload {SessionId} merged and verified. Processing .apkg...", session.SessionId);
        var result = await processor.ProcessApkgFileAsync(mergedPath, session.FileName,
            session.UserId, user, session.SkipDuplicate, session.AllowDowngrade);
        return new ChunkedUploadStatus
        {
            Status = result.StatusCode is >= 200 and < 300 && result.Summary.Errors.Count == 0 ? "Succeeded" : "Failed",
            StatusCode = result.StatusCode,
            Result = JsonSerializer.SerializeToElement(result.Summary, JsonOptions),
            CompletedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<ChunkedUploadSession?> LoadCoreAsync(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out _))
            return null;
        var path = Path.Combine(GetSessionDirectory(sessionId), "session.json");
        if (!File.Exists(path))
            return null;
        var session = JsonSerializer.Deserialize<ChunkedUploadSession>(await File.ReadAllTextAsync(path), JsonOptions)!;
        if (session.Completion?.Status is "Queued" or "Processing")
        {
            var task = session.JobId.HasValue
                ? taskQueue.GetAllTasks().FirstOrDefault(t => t.TaskId == session.JobId)
                : null;
            if (session.ProcessingInstanceId != _instanceId || task == null ||
                task.Status is TaskExecutionStatus.Cancelled or TaskExecutionStatus.Failed)
            {
                session.Completion = Failure(
                    "Upload processing was interrupted. Some packages may already be imported. Check repository contents before retrying.");
                await SaveAsync(session);
            }
        }
        return session;
    }

    public async Task CleanupExpiredAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (var dir in Directory.GetDirectories(folders.GetChunkedUploadsFolder()))
            {
                try
                {
                    var session = await LoadCoreAsync(Path.GetFileName(dir));
                    if (session?.Completion?.Status is "Queued" or "Processing")
                        continue;
                    var retainedSince = session?.Completion?.CompletedAtUtc ?? session?.CreatedAtUtc ?? Directory.GetCreationTimeUtc(dir);
                    if (retainedSince < cutoff)
                        Directory.Delete(dir, recursive: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up upload session {SessionDirectory}", dir);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(ChunkedUploadSession session)
    {
        var path = Path.Combine(GetSessionDirectory(session.SessionId), "session.json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(session, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private void CleanupFiles(string sessionId)
    {
        try
        {
            foreach (var file in Directory.GetFiles(GetSessionDirectory(sessionId)))
                if (Path.GetFileName(file) != "session.json")
                    File.Delete(file);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up files for upload {SessionId}", sessionId);
        }
    }

    private static ChunkedUploadStatus Failure(string error, int statusCode = 500) => new()
    {
        Status = "Failed",
        StatusCode = statusCode,
        Result = JsonSerializer.SerializeToElement(new { error }),
        CompletedAtUtc = DateTime.UtcNow
    };

    public void Dispose() => _gate.Dispose();
}
