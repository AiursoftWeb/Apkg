using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for the chunked upload API:
///   POST /api/upload/init
///   PUT  /api/upload/{sessionId}/chunks/{chunkIndex}
///   POST /api/upload/{sessionId}/complete
/// </summary>
[TestClass]
public class ChunkedUploadTests : TestBase
{
    private ApkgDbContext _db = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task<string> CreateApiKeyAsync(bool withManageRepos = false)
    {
        var userManager = GetService<UserManager<User>>();
        var email = $"chunked-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Chunked Upload Test User"
        };
        var result = await userManager.CreateAsync(user, "Test@123456!");
        Assert.IsTrue(result.Succeeded,
            $"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        if (withManageRepos)
        {
            await userManager.AddClaimAsync(user,
                new Claim(AppPermissions.Type, AppPermissionNames.CanManageRepositories));
            await userManager.AddClaimAsync(user,
                new Claim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories));
        }

        var rawKey = $"apkgkey{Guid.NewGuid():N}";
        _db.UserApiKeys.Add(new UserApiKey
        {
            UserId = user.Id,
            Name = "Chunked Upload Test Key",
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKey),
            KeyPrefix = rawKey[..8]
        });
        await _db.SaveChangesAsync();
        return rawKey;
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private static byte[] CreateApkgArchive(string manifestXml,
        params (string fileName, byte[] content)[] files)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms,
                   System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            using var tar = new System.Formats.Tar.TarWriter(gz,
                System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true);

            var manifestBytes = Encoding.UTF8.GetBytes(manifestXml);
            var manifestEntry = new System.Formats.Tar.PaxTarEntry(
                System.Formats.Tar.TarEntryType.RegularFile, "manifest.xml")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tar.WriteEntryAsync(manifestEntry).GetAwaiter().GetResult();

            foreach (var (name, data) in files)
            {
                var entry = new System.Formats.Tar.PaxTarEntry(
                    System.Formats.Tar.TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(data)
                };
                tar.WriteEntryAsync(entry).GetAwaiter().GetResult();
            }
        }
        return ms.ToArray();
    }

    private async Task<string> InitSessionAsync(string apiKey, byte[] fileData, int chunkCount,
        bool skipDuplicate = false, bool allowDowngrade = false)
    {
        var hash = ComputeSha256Hex(fileData);
        var initBody = JsonSerializer.Serialize(new
        {
            fileName = "test.apkg",
            totalSize = fileData.Length,
            chunkCount,
            fileHash = hash,
            skipDuplicate,
            allowDowngrade
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/init");
        request.Content = new StringContent(initBody, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"Init failed: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("sessionId").GetString()!;
    }

    private async Task UploadChunkAsync(string apiKey, string sessionId, int chunkIndex, byte[] chunkData)
    {
        using var content = new ByteArrayContent(chunkData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/upload/{sessionId}/chunks/{chunkIndex}");
        request.Content = content;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"Chunk {chunkIndex} upload failed: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<HttpResponseMessage> CompleteAsync(string apiKey, string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/upload/{sessionId}/complete");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return await Http.SendAsync(request);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Authentication
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Init_NoAuth_Returns401()
    {
        var body = JsonSerializer.Serialize(new
        {
            fileName = "test.apkg",
            totalSize = 1024,
            chunkCount = 1,
            fileHash = ComputeSha256Hex(new byte[1024])
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/init");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Init
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Init_ValidRequest_ReturnsSessionId()
    {
        var apiKey = await CreateApiKeyAsync();
        var fileData = new byte[1024];
        new Random(42).NextBytes(fileData);

        var sessionId = await InitSessionAsync(apiKey, fileData, chunkCount: 2);
        Assert.IsFalse(string.IsNullOrWhiteSpace(sessionId));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Upload Chunk
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UploadChunk_InvalidSession_Returns404()
    {
        var apiKey = await CreateApiKeyAsync();
        using var content = new ByteArrayContent(new byte[64]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Put,
            "/api/upload/nonexistent/chunks/0");
        request.Content = content;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadChunk_WrongUser_Returns403()
    {
        var apiKeyA = await CreateApiKeyAsync();
        var apiKeyB = await CreateApiKeyAsync();

        var fileData = new byte[1024];
        new Random(42).NextBytes(fileData);

        // Init with user A
        var sessionId = await InitSessionAsync(apiKeyA, fileData, chunkCount: 1);

        // Upload chunk with user B
        using var content = new ByteArrayContent(fileData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/upload/{sessionId}/chunks/0");
        request.Content = content;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyB);

        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadChunk_OutOfRange_Returns400()
    {
        var apiKey = await CreateApiKeyAsync();
        var fileData = new byte[1024];
        new Random(42).NextBytes(fileData);

        var sessionId = await InitSessionAsync(apiKey, fileData, chunkCount: 2);

        using var content = new ByteArrayContent(new byte[64]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // Index 2 is out of range for chunkCount=2 (valid: 0, 1)
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/upload/{sessionId}/chunks/2");
        request.Content = content;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadChunk_ValidChunk_Returns200()
    {
        var apiKey = await CreateApiKeyAsync();
        var fileData = new byte[1024];
        new Random(42).NextBytes(fileData);

        var sessionId = await InitSessionAsync(apiKey, fileData, chunkCount: 2);
        await UploadChunkAsync(apiKey, sessionId, 0, fileData[..512]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Complete
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Complete_MissingChunks_Returns400()
    {
        var apiKey = await CreateApiKeyAsync();
        var fileData = new byte[1024];
        new Random(42).NextBytes(fileData);

        var sessionId = await InitSessionAsync(apiKey, fileData, chunkCount: 2);

        // Only upload chunk 0, skip chunk 1
        await UploadChunkAsync(apiKey, sessionId, 0, fileData[..512]);

        var response = await CompleteAsync(apiKey, sessionId);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(body.Contains("Missing"), "Error should mention missing chunks.");
    }

    [TestMethod]
    public async Task Complete_HashMismatch_Returns400()
    {
        var apiKey = await CreateApiKeyAsync();
        var fileData = new byte[1024];
        new Random(42).NextBytes(fileData);

        var sessionId = await InitSessionAsync(apiKey, fileData, chunkCount: 1);

        // Upload different data than what was hashed during init
        var wrongData = new byte[1024];
        new Random(99).NextBytes(wrongData);
        await UploadChunkAsync(apiKey, sessionId, 0, wrongData);

        var response = await CompleteAsync(apiKey, sessionId);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(body.Contains("Hash mismatch"), "Error should mention hash mismatch.");
    }

    [TestMethod]
    public async Task Complete_ValidUpload_ProcessesApkg()
    {
        var apiKey = await CreateApiKeyAsync(withManageRepos: true);

        // Build a real .apkg archive
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>chunked-test-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>chunked-test-pkg_1.0.0_noble_amd64.deb</DebFile>
                  <Suite>noble-addon</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("chunked-test-pkg_1.0.0_noble_amd64.deb", new byte[64]));

        // Split into 2 chunks
        var chunkSize = apkgBytes.Length / 2;
        var chunk0 = apkgBytes[..chunkSize];
        var chunk1 = apkgBytes[chunkSize..];

        var sessionId = await InitSessionAsync(apiKey, apkgBytes, chunkCount: 2);
        await UploadChunkAsync(apiKey, sessionId, 0, chunk0);
        await UploadChunkAsync(apiKey, sessionId, 1, chunk1);

        var response = await CompleteAsync(apiKey, sessionId);

        // The server should process the .apkg (even if no repo matches — returns OK with warnings)
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"Complete should return OK. Body: {await response.Content.ReadAsStringAsync()}");
    }

    [TestMethod]
    public async Task Complete_ValidUpload_EndToEnd_NoMatchingRepo_RecordDeleted()
    {
        var apiKey = await CreateApiKeyAsync(withManageRepos: true);

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>chunked-e2e-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>chunked-e2e-pkg.deb</DebFile>
                  <Suite>noble-addon</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("chunked-e2e-pkg.deb", new byte[64]));

        // Upload as a single chunk
        var sessionId = await InitSessionAsync(apiKey, apkgBytes, chunkCount: 1);
        await UploadChunkAsync(apiKey, sessionId, 0, apkgBytes);

        var response = await CompleteAsync(apiKey, sessionId);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // Revision record should be cleaned up (no matching repo → nothing uploaded)
        var uploadCount = _db.ApkgRevisions.Count();
        Assert.AreEqual(0, uploadCount,
            "Upload record should be deleted when nothing was uploaded.");
    }

    [TestMethod]
    public async Task Complete_InvalidApkg_Returns400()
    {
        var apiKey = await CreateApiKeyAsync();

        // Random bytes, not a valid .apkg
        var garbage = new byte[256];
        new Random(42).NextBytes(garbage);

        var sessionId = await InitSessionAsync(apiKey, garbage, chunkCount: 1);
        await UploadChunkAsync(apiKey, sessionId, 0, garbage);

        var response = await CompleteAsync(apiKey, sessionId);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "Invalid .apkg data should return 400.");
    }

    [TestMethod]
    public async Task Complete_SessionCleanedUp_AfterSuccess()
    {
        var apiKey = await CreateApiKeyAsync(withManageRepos: true);

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>cleanup-test-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>cleanup-test-pkg.deb</DebFile>
                  <Suite>noble-addon</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("cleanup-test-pkg.deb", new byte[64]));

        var sessionId = await InitSessionAsync(apiKey, apkgBytes, chunkCount: 1);
        await UploadChunkAsync(apiKey, sessionId, 0, apkgBytes);

        var response = await CompleteAsync(apiKey, sessionId);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // Trying to complete again should return 404 (session cleaned up)
        var response2 = await CompleteAsync(apiKey, sessionId);
        Assert.AreEqual(HttpStatusCode.NotFound, response2.StatusCode,
            "Session should be cleaned up after successful completion.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Cleanup Job
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CleanupJob_RemovesStaleSessions()
    {
        var folders = GetService<Aiursoft.Apkg.Services.FileStorage.FeatureFoldersProvider>();
        var chunkedRoot = folders.GetChunkedUploadsFolder();

        // Create a "stale" session directory (created 25 hours ago)
        var staleSessionId = Guid.NewGuid().ToString("N");
        var staleDir = Path.Combine(chunkedRoot, staleSessionId);
        Directory.CreateDirectory(staleDir);
        var staleSession = new ChunkedUploadSession
        {
            SessionId = staleSessionId,
            FileName = "stale.apkg",
            TotalSize = 1024,
            ChunkCount = 1,
            FileHash = ComputeSha256Hex(new byte[1024]),
            UserId = "fake-user",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-25),
            SkipDuplicate = false,
            AllowDowngrade = false
        };
        await File.WriteAllTextAsync(
            Path.Combine(staleDir, "session.json"),
            JsonSerializer.Serialize(staleSession,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        // Write a fake chunk file to verify full directory deletion
        await File.WriteAllBytesAsync(Path.Combine(staleDir, "chunk_0"), new byte[64]);

        // Create a "fresh" session directory (just now)
        var freshSessionId = Guid.NewGuid().ToString("N");
        var freshDir = Path.Combine(chunkedRoot, freshSessionId);
        Directory.CreateDirectory(freshDir);
        var freshSession = new ChunkedUploadSession
        {
            SessionId = freshSessionId,
            FileName = "fresh.apkg",
            TotalSize = 1024,
            ChunkCount = 1,
            FileHash = ComputeSha256Hex(new byte[1024]),
            UserId = "fake-user",
            CreatedAtUtc = DateTime.UtcNow,
            SkipDuplicate = false,
            AllowDowngrade = false
        };
        await File.WriteAllTextAsync(
            Path.Combine(freshDir, "session.json"),
            JsonSerializer.Serialize(freshSession,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        // Run the cleanup job
        var cleanupJob = GetService<Aiursoft.Apkg.Services.BackgroundJobs.ApkgTempCleanupJob>();
        await cleanupJob.ExecuteAsync();

        // Stale session should be deleted
        Assert.IsFalse(Directory.Exists(staleDir),
            "Stale session directory should be deleted by cleanup job.");

        // Fresh session should remain
        Assert.IsTrue(Directory.Exists(freshDir),
            "Fresh session directory should NOT be deleted by cleanup job.");

        // Clean up the fresh dir ourselves
        Directory.Delete(freshDir, recursive: true);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Concurrent Retry (File Locking Bug)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression test: when the client SDK retries a chunk upload (sending the same
    /// chunk data again while a previous attempt is still in flight), the server must
    /// handle the concurrent writes without throwing "file being used by another process".
    ///
    /// The fix writes each incoming chunk to a unique temp file and then atomically
    /// moves it into place, so concurrent uploads of the same chunk never contend on
    /// the same file handle.
    /// </summary>
    [TestMethod]
    public async Task UploadChunk_ConcurrentRetriesOfSameChunk_AllSucceed_AndMergedContentIsCorrect()
    {
        // Arrange — build a valid .apkg and split into 2 chunks
        var apiKey = await CreateApiKeyAsync(withManageRepos: true);

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>concurrent-retry-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>concurrent-retry-pkg_1.0.0_noble_amd64.deb</DebFile>
                  <Suite>noble-addon</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("concurrent-retry-pkg_1.0.0_noble_amd64.deb", new byte[64]));

        var chunkSize = apkgBytes.Length / 2;
        var chunk0 = apkgBytes[..chunkSize];
        var chunk1 = apkgBytes[chunkSize..];

        var sessionId = await InitSessionAsync(apiKey, apkgBytes, chunkCount: 2);

        // Upload chunk 0 normally
        await UploadChunkAsync(apiKey, sessionId, 0, chunk0);

        // Act — fire 3 concurrent uploads of the SAME chunk (simulates SDK retry)
        var retryCount = 3;
        var concurrentTasks = Enumerable.Range(0, retryCount).Select(async _ =>
        {
            // Each request needs its own HttpContent/HttpRequestMessage
            using var content = new ByteArrayContent(chunk1);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var request = new HttpRequestMessage(HttpMethod.Put,
                $"/api/upload/{sessionId}/chunks/1");
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            return await Http.SendAsync(request);
        }).ToArray();

        var responses = await Task.WhenAll(concurrentTasks);

        // Assert — every concurrent upload must succeed (no 500 from file locking)
        for (var i = 0; i < responses.Length; i++)
        {
            var body = await responses[i].Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, responses[i].StatusCode,
                $"Concurrent chunk upload #{i} must return 200. Got {responses[i].StatusCode}: {body}");
        }

        // Assert — completing the upload must succeed and the hash must match
        var completeResponse = await CompleteAsync(apiKey, sessionId);
        var completeBody = await completeResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, completeResponse.StatusCode,
            $"Complete must succeed after concurrent chunk uploads. Body: {completeBody}");
    }

    /// <summary>
    /// When a chunk file already exists (from a previous completed attempt) and the
    /// client retries that same chunk, the server must still accept it and the final
    /// content must remain correct.
    ///
    /// This simulates the case where chunk N succeeded but the client didn't receive
    /// the 200 response (network hiccup), so it retries. The server must handle this
    /// idempotently — overwriting the chunk with the same data is harmless.
    /// </summary>
    [TestMethod]
    public async Task UploadChunk_SequentialRetriesOfSameChunk_AllSucceed_AndMergedContentIsCorrect()
    {
        var apiKey = await CreateApiKeyAsync(withManageRepos: true);

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>sequential-retry-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>sequential-retry-pkg_1.0.0_noble_amd64.deb</DebFile>
                  <Suite>noble-addon</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("sequential-retry-pkg_1.0.0_noble_amd64.deb", new byte[64]));

        var sessionId = await InitSessionAsync(apiKey, apkgBytes, chunkCount: 1);

        // Upload the same chunk 3 times sequentially (simulates retry after response lost)
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await UploadChunkAsync(apiKey, sessionId, 0, apkgBytes);
        }

        // Complete must succeed with correct hash
        var completeResponse = await CompleteAsync(apiKey, sessionId);
        var completeBody = await completeResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, completeResponse.StatusCode,
            $"Complete must succeed after sequential retries. Body: {completeBody}");
    }

    /// <summary>
    /// If a client accidentally sends different data for the same chunk index
    /// (corrupted retry), the eventual hash check during Complete must catch it
    /// and return 400.
    /// </summary>
    [TestMethod]
    public async Task UploadChunk_DifferentDataInRetries_CausesHashMismatch()
    {
        var apiKey = await CreateApiKeyAsync(withManageRepos: true);

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>corrupted-retry-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>corrupted-retry-pkg_1.0.0_noble_amd64.deb</DebFile>
                  <Suite>noble-addon</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("corrupted-retry-pkg_1.0.0_noble_amd64.deb", new byte[64]));

        var sessionId = await InitSessionAsync(apiKey, apkgBytes, chunkCount: 1);

        // First upload: correct data
        await UploadChunkAsync(apiKey, sessionId, 0, apkgBytes);

        // Retry: different (corrupted) data — silently overwrites the chunk
        var corruptedData = new byte[apkgBytes.Length];
        new Random(42).NextBytes(corruptedData);

        using var content = new ByteArrayContent(corruptedData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/upload/{sessionId}/chunks/0");
        request.Content = content;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var chunkResponse = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, chunkResponse.StatusCode,
            "Chunk upload should return 200 (server doesn't validate per-chunk hash).");

        // Complete must detect the hash mismatch
        var completeResponse = await CompleteAsync(apiKey, sessionId);
        var completeBody = await completeResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.BadRequest, completeResponse.StatusCode,
            $"Complete must detect hash mismatch from corrupted retry. Body: {completeBody}");
        Assert.IsTrue(completeBody.Contains("Hash mismatch"),
            $"Error should mention hash mismatch. Got: {completeBody}");
    }
}
