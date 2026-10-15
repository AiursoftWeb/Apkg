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

    /// <summary>Downloads a package and verifies the checksum from its index.</summary>
    public async Task DownloadPackageAsync(DebianPackage package, string destinationPath, Action<long, long>? progress = null)
    {
        var useSha512 = string.IsNullOrWhiteSpace(package.SHA256);
        var expectedHash = useSha512 ? package.SHA512 : package.SHA256;
        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new InvalidDataException("Package must contain a SHA256 or SHA512 checksum.");
        var url = $"{_repository.BaseUrl}{package.Filename}";
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tempFile = destinationPath + ".tmp";
        using var client = _httpClientFactory();
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var stream = await response.Content.ReadAsStreamAsync();
            using (var output = File.Create(tempFile))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                    progress?.Invoke(totalRead, totalBytes);
                }
            }

            using (var input = File.OpenRead(tempFile))
            using (System.Security.Cryptography.HashAlgorithm hasher = useSha512
                       ? System.Security.Cryptography.SHA512.Create()
                       : System.Security.Cryptography.SHA256.Create())
            {
                var actualHash = Convert.ToHexStringLower(await hasher.ComputeHashAsync(input));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new System.Security.SecurityException($"Package hash mismatch! \nUrl: {url}\nExpected: {expectedHash}\nActual:   {actualHash}");
            }
            File.Move(tempFile, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
