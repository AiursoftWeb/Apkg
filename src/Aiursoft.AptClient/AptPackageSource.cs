using System.IO.Compression;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Aiursoft.AptClient.Tests")]

namespace Aiursoft.AptClient;

using Abstractions;

public class AptPackageSource
{
    private readonly UpstreamAptSource _repository;

    public string Component { get; }
    public string Arch { get; }

    // Legacy properties for compatibility if needed (or remove them)
    public string ServerUrl => _repository.BaseUrl;
    public string Suite => _repository.Suite;

    private readonly Func<HttpClient> _httpClientFactory;

    public AptPackageSource(UpstreamAptSource repository, string component, string arch, Func<HttpClient>? httpClientFactory = null)
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
    /// Downloads a specific package (.deb) and verifies its strongest advertised checksum.
    /// </summary>
    public async Task DownloadPackageAsync(DebianPackage package, string destinationPath, Action<long, long>? progress = null)
    {
        var checksum = AptChecksum.ForPackage(package);

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

            var buffer = new byte[8192];
            int bytesRead;
            long totalRead = 0;

            // Use a temporary file for download to avoid partial files
            var tempFile = destinationPath + ".tmp";

            using (var tempFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await tempFs.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    progress?.Invoke(totalRead, totalBytes);
                }
            }

            // Verify
            using (var verifyStream = File.OpenRead(tempFile))
            using (var hasher = checksum.CreateHasher())
            {
                var hashBytes = await hasher.ComputeHashAsync(verifyStream);
                var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                if (!string.Equals(actualHash, checksum.Value, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempFile);
                    throw new System.Security.SecurityException($"Package hash mismatch! \nUrl: {url}\nExpected: {checksum.Value}\nActual:   {actualHash}");
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
