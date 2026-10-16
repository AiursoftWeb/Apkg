using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.AptClient;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>Fetches explicitly declared payloads through the existing verified APT client.</summary>
public class PackageFetchService(ILogger<PackageFetchService> logger, Func<HttpClient>? httpClientFactory = null)
{
    public const string ManifestFormat = "apkg-build-inputs-v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Validate(IReadOnlyList<FetchInput> inputs)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            if (!Regex.IsMatch(input.Id, @"^[a-zA-Z0-9][a-zA-Z0-9_.+-]*$") || !ids.Add(input.Id))
                throw new InvalidDataException($"Invalid or duplicate build input Id '{input.Id}'. Use distinct Id values for different architectures.");
            if (!Regex.IsMatch(input.Package, @"^[a-z0-9][a-z0-9+.-]*$"))
                throw new InvalidDataException($"Invalid package name '{input.Package}'.");
            if (!Regex.IsMatch(input.Arch, @"^[a-z0-9][a-z0-9-]*$"))
                throw new InvalidDataException($"An explicit Debian architecture is required for '{input.Id}'.");
            if (input.Sources.Count == 0) throw new InvalidDataException($"No sources for '{input.Id}'.");
            foreach (var source in input.Sources)
            {
                if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                    throw new InvalidDataException($"Repository must be an explicit HTTP(S) base URL: '{source.Url}'.");
                if (!Regex.IsMatch(source.Suite, @"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$") ||
                    !Regex.IsMatch(source.Component, @"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$"))
                    throw new InvalidDataException("Explicit suite and component names are required; flat repositories are not supported.");
                if (string.IsNullOrWhiteSpace(source.SignedBy) || !File.Exists(source.SignedBy))
                    throw new InvalidDataException($"Signed-By keyring file not found: '{source.SignedBy}'.");
            }
        }
    }

    public async Task<FetchManifest> FetchAsync(IReadOnlyList<FetchInput> inputs, string output, bool extract)
    {
        Validate(inputs);
        BuildInputResolver.Require(output, "output directory");
        output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        var parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("Output cannot be a filesystem root.");
        Directory.CreateDirectory(parent);
        // Keep the lock file: unlinking it would allow a third process to bypass an existing waiter.
        using var outputLock = new FileStream(output + ".fetch.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        EnsureOwnedOutput(output);
        var work = Path.Combine(parent, $".apkg-fetch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var repositories = new Dictionary<(string, string, string), UpstreamAptSource>();
            var indexes = new Dictionary<(FetchSource, string), List<DebianPackageFromApt>>();
            var wanted = inputs.Select(i => i.Package).ToHashSet(StringComparer.Ordinal);
            var selected = new List<(FetchInput Input, FetchSource Source, DebianPackageFromApt Candidate)>();
            var comparer = new AptVersionComparisonService();
            foreach (var input in inputs)
            {
                (FetchSource Source, DebianPackageFromApt Candidate)? best = null;
                foreach (var source in input.Sources.Distinct())
                {
                    var key = (source.Url.TrimEnd('/'), source.Suite, Path.GetFullPath(source.SignedBy));
                    if (!repositories.TryGetValue(key, out var repository))
                    {
                        repository = new UpstreamAptSource(key.Item1, source.Suite, key.Item3,
                            allowInsecure: false, httpClientFactory: httpClientFactory);
                        repositories.Add(key, repository);
                    }
                    try
                    {
                        var files = (await repository.GetSupportedFilesAsync()).ToHashSet(StringComparer.Ordinal);
                        var foundIndex = false;
                        foreach (var arch in new[] { input.Arch, "all" }.Distinct())
                        {
                            if (!new[] { "Packages.xz", "Packages.gz", "Packages" }.Any(f => files.Contains($"{source.Component}/binary-{arch}/{f}")))
                                continue;
                            foundIndex = true;
                            if (!indexes.TryGetValue((source, arch), out var candidates))
                            {
                                logger.LogInformation("Reading verified index: {Repository} {Suite}/{Component}/{Arch}", source.Url, source.Suite, source.Component, arch);
                                candidates = [];
                                var packageSource = new AptPackageSource(repository, source.Component, arch, httpClientFactory);
                                await foreach (var candidate in packageSource.FetchPackagesAsync())
                                    if (wanted.Contains(candidate.Package.Package)) candidates.Add(candidate);
                                indexes.Add((source, arch), candidates);
                            }
                            foreach (var candidate in candidates.Where(c => c.Package.Package == input.Package &&
                                         (c.Package.Architecture == input.Arch || c.Package.Architecture == "all")))
                            {
                                var comparison = best == null ? 1 : comparer.Compare(candidate.Package.Version, best.Value.Candidate.Package.Version);
                                if (comparison > 0) best = (source, candidate);
                                else if (comparison == 0 && best!.Value.Candidate.Package.Architecture == candidate.Package.Architecture &&
                                         !string.Equals(best.Value.Candidate.Package.SHA256, candidate.Package.SHA256, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidDataException($"Conflicting hashes for {input.Package} {candidate.Package.Version}; narrow the declared sources.");
                            }
                        }
                        if (!foundIndex) throw new InvalidDataException($"No index for {source.Component}/{input.Arch} or all.");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Cannot query required source {source.Url} {source.Suite}/{source.Component}: {ex.Message}", ex);
                    }
                }
                if (best == null) throw new InvalidOperationException($"Package '{input.Package}' ({input.Arch}) was not found in the declared sources.");
                selected.Add((input, best.Value.Source, best.Value.Candidate));
            }

            var fetched = new List<FetchedInput>();
            foreach (var (input, source, candidate) in selected)
            {
                var package = candidate.Package;
                if (!Regex.IsMatch(package.Version, @"^[0-9][a-zA-Z0-9.+:~\-]*$") ||
                    !Regex.IsMatch(package.SHA256, @"^[a-fA-F0-9]{64}$") ||
                    !Regex.IsMatch(package.Filename, @"^[a-zA-Z0-9][a-zA-Z0-9_./+%:~\-]*$") ||
                    package.Filename.Split('/').Any(s => s is "." or ".."))
                    throw new InvalidDataException($"Invalid download metadata for '{input.Id}'.");
                var relativeDeb = $"debs/{input.Id}/{package.Package}_{package.Version}_{package.Architecture}.deb";
                var destination = Path.Combine(work, relativeDeb);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var cached = Path.Combine(output, relativeDeb);
                if (File.Exists(cached) && await HasHashAsync(cached, package.SHA256))
                {
                    File.Copy(cached, destination);
                    logger.LogInformation("Reusing verified package: {Package} {Version} ({Arch})", package.Package, package.Version, package.Architecture);
                }
                else
                {
                    logger.LogInformation("Downloading {Package} {Version} ({Arch}) from {Suite}", package.Package, package.Version, package.Architecture, source.Suite);
                    await candidate.Source.DownloadPackageAsync(package, destination);
                }
                string? extracted = null;
                if (extract)
                {
                    extracted = $"extracted/{input.Id}";
                    await ExtractAsync(destination, Path.Combine(work, extracted));
                }
                fetched.Add(new FetchedInput(input.Id, package.Package, package.Version, package.Architecture,
                    input.Arch, source.Url, source.Suite, source.Component, package.SHA256, relativeDeb, extracted));
            }
            var manifest = new FetchManifest(ManifestFormat, fetched);
            await File.WriteAllTextAsync(Path.Combine(work, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            Publish(work, output);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        }
    }

    private static async Task<bool> HasHashAsync(string path, string expected)
    {
        await using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream)), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExtractAsync(string deb, string directory)
    {
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dpkg-deb") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--extract");
        start.ArgumentList.Add(deb);
        start.ArgumentList.Add(directory);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dpkg-deb.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException($"dpkg-deb failed ({process.ExitCode}): {error}");
    }

    private static void EnsureOwnedOutput(string output)
    {
        if (new DirectoryInfo(output).LinkTarget != null || File.Exists(output))
            throw new IOException("Output must be a directory, not a file or symlink.");
        if (!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any()) return;
        var manifestPath = Path.Combine(output, "manifest.json");
        if (!File.Exists(manifestPath) || JsonSerializer.Deserialize<FetchManifest>(File.ReadAllText(manifestPath))?.Format != ManifestFormat)
            throw new IOException("Output is not an apkg fetch directory. Choose an empty directory; existing files will not be overwritten.");
    }

    private static void Publish(string work, string output)
    {
        var backup = output + $".old-{Guid.NewGuid():N}";
        var existed = Directory.Exists(output);
        if (existed) Directory.Move(output, backup);
        try { Directory.Move(work, output); }
        catch
        {
            if (existed) Directory.Move(backup, output);
            throw;
        }
        if (existed) Directory.Delete(backup, recursive: true);
    }
}
