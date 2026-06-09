using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aiursoft.Apkg.Sdk.Services;

public class ApkgPushService(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Pushes an .apkg file to the server using a single multipart upload.
    /// Returns the JSON response body as a string.
    /// Throws HttpRequestException on network failure.
    /// Throws InvalidOperationException with server error message on HTTP error.
    /// </summary>
    public async Task<string> PushAsync(string apkgFilePath, string serverUrl, string apiKey, bool skipDuplicate, bool allowDowngrade = false)
    {
        serverUrl = serverUrl.TrimEnd('/');

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(apkgFilePath);
        using var fileContent = CreateApkgFileContent(fileStream);
        content.Add(fileContent, "apkg", Path.GetFileName(apkgFilePath));

        var url = $"{serverUrl}/api/packages/apkg-upload?skipDuplicate={skipDuplicate}&allowDowngrade={allowDowngrade}";
        using var request = CreateRequest(url, content, apiKey);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode
            && response.StatusCode != HttpStatusCode.Conflict
            && response.StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException($"Server returned {(int)response.StatusCode}: {body}");

        return body;
    }

    /// <summary>
    /// Pushes an .apkg file using chunked upload (init → chunks → complete).
    /// Each chunk is retried up to 3 times with exponential backoff.
    /// If the file is smaller than <paramref name="chunkSize"/>, falls back to <see cref="PushAsync"/>.
    /// </summary>
    /// <param name="apkgFilePath">Path to the .apkg file to push.</param>
    /// <param name="serverUrl">Package source URL (e.g. https://apkg.aiursoft.com).</param>
    /// <param name="apiKey">The API key for the server.</param>
    /// <param name="skipDuplicate">If true, skip duplicate packages.</param>
    /// <param name="allowDowngrade">If true, allow uploading older versions.</param>
    /// <param name="chunkSize">Maximum size of each upload chunk in bytes.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <returns>The JSON response body as a string.</returns>
    public async Task<string> PushChunkedAsync(
        string apkgFilePath,
        string serverUrl,
        string apiKey,
        bool skipDuplicate,
        bool allowDowngrade,
        long chunkSize,
        IProgress<ChunkedUploadProgress>? progress = null)
    {
        var fileInfo = new FileInfo(apkgFilePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($".apkg file not found: {apkgFilePath}");

        // Fall back to single upload for small files
        if (fileInfo.Length <= chunkSize)
            return await PushAsync(apkgFilePath, serverUrl, apiKey, skipDuplicate, allowDowngrade);

        serverUrl = serverUrl.TrimEnd('/');
        var totalSize = fileInfo.Length;
        var chunkCount = (int)Math.Ceiling((double)totalSize / chunkSize);

        // Step 1: Compute SHA-256 of the entire file (streaming)
        progress?.Report(new ChunkedUploadProgress { Phase = "Hashing", Message = "Computing file hash..." });
        var fileHash = await ComputeSha256Async(apkgFilePath);

        // Step 2: Init
        progress?.Report(new ChunkedUploadProgress { Phase = "Init", Message = "Initializing upload session..." });
        var initBody = JsonSerializer.Serialize(new
        {
            fileName = Path.GetFileName(apkgFilePath),
            totalSize,
            chunkCount,
            fileHash,
            skipDuplicate,
            allowDowngrade
        }, JsonOptions);

        using var initRequest = new HttpRequestMessage(HttpMethod.Post, $"{serverUrl}/api/upload/init")
        {
            Content = new StringContent(initBody, Encoding.UTF8, "application/json")
        };
        initRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var initResponse = await httpClient.SendAsync(initRequest);
        var initResponseBody = await initResponse.Content.ReadAsStringAsync();
        if (!initResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Init failed ({(int)initResponse.StatusCode}): {initResponseBody}");

        using var initDoc = JsonDocument.Parse(initResponseBody);
        var sessionId = initDoc.RootElement.GetProperty("sessionId").GetString()
            ?? throw new InvalidOperationException("Server did not return a sessionId.");

        // Step 3: Upload chunks sequentially with retry.
        // Re-open the file so the stream position starts at 0. The hash computation above
        // consumed a separate stream; reopening also guards against (unlikely) external
        // modifications between hashing and chunked reading.
        await using var fileStream = File.OpenRead(apkgFilePath);
        var buffer = new byte[chunkSize];

        for (var i = 0; i < chunkCount; i++)
        {
            var bytesToRead = (int)Math.Min(chunkSize, totalSize - (long)i * chunkSize);
            var bytesRead = 0;
            while (bytesRead < bytesToRead)
            {
                var read = await fileStream.ReadAsync(buffer.AsMemory(bytesRead, bytesToRead - bytesRead));
                if (read == 0)
                    throw new InvalidOperationException($"Unexpected end of file at chunk {i}.");
                bytesRead += read;
            }

            var chunkData = buffer.AsMemory(0, bytesRead);

            progress?.Report(new ChunkedUploadProgress
            {
                Phase = "Uploading",
                Message = $"Uploading chunk {i + 1}/{chunkCount} ({bytesRead / (1024 * 1024)}MB)...",
                ChunkIndex = i,
                ChunkCount = chunkCount
            });

            await UploadChunkWithRetryAsync(serverUrl, sessionId, apiKey, i, chunkData);
        }

        // Step 4: Complete
        progress?.Report(new ChunkedUploadProgress { Phase = "Completing", Message = "Finalizing upload..." });

        using var completeRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{serverUrl}/api/upload/{sessionId}/complete");
        completeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var completeResponse = await httpClient.SendAsync(completeRequest);
        var completeBody = await completeResponse.Content.ReadAsStringAsync();

        if (!completeResponse.IsSuccessStatusCode
            && completeResponse.StatusCode != HttpStatusCode.Conflict
            && completeResponse.StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException($"Complete failed ({(int)completeResponse.StatusCode}): {completeBody}");

        return completeBody;
    }

    private async Task UploadChunkWithRetryAsync(
        string serverUrl, string sessionId, string apiKey,
        int chunkIndex, ReadOnlyMemory<byte> chunkData,
        int maxRetries = 3)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Note: .ToArray() copies the chunk buffer (~90 MB) onto the LOH.
                // ByteArrayContent stores a byte[] internally, so there is no zero-copy
                // HttpContent alternative in .NET. Acceptable for sequential uploads.
                using var chunkContent = new ByteArrayContent(chunkData.ToArray());
                chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var chunkRequest = new HttpRequestMessage(HttpMethod.Put,
                    $"{serverUrl}/api/upload/{sessionId}/chunks/{chunkIndex}")
                {
                    Content = chunkContent
                };
                chunkRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var chunkResponse = await httpClient.SendAsync(chunkRequest);
                if (chunkResponse.IsSuccessStatusCode)
                    return;

                var errorBody = await chunkResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Chunk {chunkIndex} upload failed ({(int)chunkResponse.StatusCode}): {errorBody}");
            }
            catch (Exception) when (attempt < maxRetries)
            {
                // Exponential backoff: 1s, 2s, 4s
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static StreamContent CreateApkgFileContent(Stream fileStream)
    {
        var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static HttpRequestMessage CreateRequest(string url, HttpContent content, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }
}

/// <summary>
/// Progress information for chunked upload operations.
/// </summary>
public class ChunkedUploadProgress
{
    public required string Phase { get; init; }
    public required string Message { get; init; }
    public int? ChunkIndex { get; init; }
    public int? ChunkCount { get; init; }
}

