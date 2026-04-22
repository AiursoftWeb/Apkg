using System.IO.Compression;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Aiursoft.AptClient.Tests")]

namespace Aiursoft.AptClient;

using Abstractions;

public class AptPackageSource
{
    private readonly AptRepository _repository;

    public string Component { get; }
    public string Arch { get; }

    // Legacy properties for compatibility if needed (or remove them)
    public string ServerUrl => _repository.BaseUrl;
    public string Suite => _repository.Suite;

    private readonly Func<HttpClient> _httpClientFactory;

    public AptPackageSource(AptRepository repository, string component, string arch, Func<HttpClient>? httpClientFactory = null)
    {
        _repository = repository;
        Component = component;
        Arch = arch;
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient());
    }

    public async IAsyncEnumerable<DebianPackageFromApt> FetchPackagesAsync(Action<string, long>? progress = null)
    {
        // 1. Get supported files from repository
        var supportedFiles = (await _repository.GetSupportedFilesAsync()).ToList();

        // 2. Construct paths in order of preference
        var relPathXz = $"{Component}/binary-{Arch}/Packages.xz";
        var relPathGz = $"{Component}/binary-{Arch}/Packages.gz";
        var relPathRaw = $"{Component}/binary-{Arch}/Packages";

        Stream? stream = null;
        
        // Try XZ first (best compression)
        if (supportedFiles.Contains(relPathXz, StringComparer.OrdinalIgnoreCase))
        {
            var rawStream = await _repository.GetValidatedStreamAsync(relPathXz, progress);
            stream = new SharpCompress.Compressors.Xz.XZStream(rawStream);
        }
        else if (supportedFiles.Contains(relPathGz, StringComparer.OrdinalIgnoreCase))
        {
            var rawStream = await _repository.GetValidatedStreamAsync(relPathGz, progress);
            stream = new GZipStream(rawStream, CompressionMode.Decompress);
        }
        else if (supportedFiles.Contains(relPathRaw, StringComparer.OrdinalIgnoreCase))
        {
            stream = await _repository.GetValidatedStreamAsync(relPathRaw, progress);
        }

        if (stream == null)
        {
            throw new FileNotFoundException($"None of the supported package formats (xz, gz, raw) were found in the repository for {Component}/{Arch} in suite {_repository.Suite}. Available files: {string.Join(", ", supportedFiles.Where(f => f.Contains($"{Component}/binary-{Arch}")))}");
        }

        // 3. Parse
        using (stream)
        {
            var dicts = DebianPackageParser.Parse(stream);
            foreach (var dict in dicts)
            {
                var pkg = DebianPackageParser.MapToPackage(dict, Suite, Component);
                yield return new DebianPackageFromApt { Package = pkg, Source = this };
            }
        }
    }

    /// <summary>
    /// Downloads a specific package (.deb) and verifies its SHA256 checksum.
    /// </summary>
    /// <summary>
    /// Downloads a specific package (.deb) and verifies its SHA256 checksum.
    /// </summary>
    public async Task DownloadPackageAsync(DebianPackage package, string destinationPath, Action<long, long>? progress = null)
    {
        // Filename in package is relative to the repository root (e.g. pool/main/a/acl/acl_2.2.53-6_amd64.deb)
        var url = $"{_repository.BaseUrl}{package.Filename}";

        // Ensure directory exists
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var client = _httpClientFactory();
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            // We need to compute hash while saving.
            // But FileStream validation is tricky if we want to stream.
            // We can wrap stream in a hashing stream or just read buffer, hash, write.

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            // CryptoStream with CryptoStreamMode.Read on the input stream is easiest?
            // Or manually buffer.

            // Let's implement manual buffering to support progress reporting + hashing
            var buffer = new byte[8192];
            int bytesRead;
            long totalRead = 0;

            // We can't transform block on SHA256 easily with just block updates in .NET standard without IncrementalHash (available in recent .NET).
            // Assuming modern .NET 6+, we have IncrementalHash.
            // If not, we have to use TransformBlock.
            // Let's us IncrementalHash if available, or just assume we can simply download to file then hash (simpler but 2x IO).
            // Given the requirement for "Professional", streaming hash is better.

            // Validating SHA256 matches:
            var expectedHash = package.SHA256;

            // Use a temporary file for download to avoid partial files
            var tempFile = destinationPath + ".tmp";

            using (var tempFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // We'll use a wrapper stream or just compute hash after?
                // Let's compute hash AFTER download to avoid complexity with IncrementalHash api availability check (though usually safe).
                // Actually, let's use a simple approach: Download to file. Then Hash the file.
                // It's robust.

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await tempFs.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    progress?.Invoke(totalRead, totalBytes);
                }
            }

            // Verify
            using (var verifyStream = File.OpenRead(tempFile))
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = await hasher.ComputeHashAsync(verifyStream);
                var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempFile);
                    throw new System.Security.SecurityException($"Package hash mismatch! \nUrl: {url}\nExpected: {expectedHash}\nActual:   {actualHash}");
                }
            }

            // Move to final destination
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            File.Move(tempFile, destinationPath);
        }
        catch
        {
            if (File.Exists(destinationPath + ".tmp")) File.Delete(destinationPath + ".tmp");
            throw;
        }
    }
}
