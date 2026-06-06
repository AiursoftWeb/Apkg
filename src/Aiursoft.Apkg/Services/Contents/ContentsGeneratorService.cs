using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aiursoft.Apkg.Services;

namespace Aiursoft.Apkg.Services.Contents;

public record ContentsPackage(string DebPath, string PackageName, string Section);

public record ContentsResult(string RawSha256, long RawSize, string GzSha256, long GzSize);

public class ContentsGeneratorService
{
    private const int FilePathColumnWidth = 56;

    /// <summary>
    /// Parses the output of <c>dpkg-deb -c</c> and returns only file/symlink/hardlink paths
    /// (no directories), with the leading <c>./</c> stripped.
    /// </summary>
    public static List<string> ParseDpkgDebContents(string dpkgDebOutput)
    {
        var paths = new List<string>();
        foreach (var line in dpkgDebOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            // First character: - (file), l (symlink), h (hardlink), d (directory), c (char), b (block)
            var type = trimmed[0];
            if (type != '-' && type != 'l' && type != 'h') continue;

            // Find "./" — the path always starts with "./" in dpkg-deb -c output.
            // After it, the path continues to the next space or end-of-line
            // (symlinks have " -> target" suffix, hardlinks have " link to ...").
            var dotSlash = trimmed.IndexOf("./", StringComparison.Ordinal);
            if (dotSlash < 0) continue;
            var path = trimmed[dotSlash..];
            var nextSpace = path.IndexOf(' ');
            if (nextSpace >= 0)
                path = path[..nextSpace];

            if (path.StartsWith("./"))
                path = path[2..];

            paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// Builds a single Contents file line: left-aligned path padded to <see cref="FilePathColumnWidth"/>
    /// followed by <c>section/package</c>.
    /// </summary>
    public static string BuildContentsLine(string filePath, string section, string package)
    {
        var paddedPath = filePath.Length >= FilePathColumnWidth
            ? filePath + " "
            : filePath.PadRight(FilePathColumnWidth);
        return paddedPath + $"{section}/{package}";
    }

    /// <summary>
    /// Generates <c>Contents-{arch}</c> and <c>Contents-{arch}.gz</c> in <paramref name="outputDir"/>
    /// from the given packages. Entries are sorted by file path.
    /// </summary>
    public static async Task<ContentsResult> GenerateContentsFilesAsync(
        string tempDir,
        string arch,
        string outputDir,
        IReadOnlyList<ContentsPackage> packages)
    {
        // Collect all file entries
        var entries = new List<(string FilePath, string Section, string Package)>();

        foreach (var pkg in packages)
        {
            List<string> files;
            try
            {
                files = await GetDebContentsAsync(pkg.DebPath, tempDir);
            }
            catch
            {
                // Skip packages whose .deb can't be read (e.g. IsVirtual ones without a local file)
                continue;
            }

            foreach (var file in files)
                entries.Add((file, pkg.Section, pkg.PackageName));
        }

        // Sort by file path
        entries.Sort((a, b) => string.CompareOrdinal(a.FilePath, b.FilePath));

        Directory.CreateDirectory(outputDir);

        var rawPath = Path.Combine(outputDir, $"Contents-{arch}");
        var gzPath = rawPath + ".gz";

        string rawSha256;
        long rawSize;
        string gzSha256;
        long gzSize;

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var rawFs = new FileStream(rawPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var gzFs = new FileStream(gzPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var rawHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var gzHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var rawHashing = new HashingStream(rawFs, rawHasher))
            await using (var gzHashing = new HashingStream(gzFs, gzHasher))
            await using (var gzipStream = new GZipStream(gzHashing, CompressionLevel.Optimal))
            {
                var rawWriter = new StreamWriter(rawHashing, utf8NoBom, leaveOpen: true);
                var gzWriter = new StreamWriter(gzipStream, utf8NoBom, leaveOpen: true);

                foreach (var (filePath, section, package) in entries)
                {
                    var line = BuildContentsLine(filePath, section, package);
                    await rawWriter.WriteLineAsync(line);
                    await gzWriter.WriteLineAsync(line);
                }

                await rawWriter.FlushAsync();
                await gzWriter.FlushAsync();
            }

            rawSha256 = BitConverter.ToString(rawHasher.GetHashAndReset()).Replace("-", "").ToLower();
            gzSha256 = BitConverter.ToString(gzHasher.GetHashAndReset()).Replace("-", "").ToLower();
            rawSize = rawFs.Length;
            gzSize = gzFs.Length;
        }

        return new ContentsResult(rawSha256, rawSize, gzSha256, gzSize);
    }

    /// <summary>
    /// Runs <c>dpkg-deb -c</c> on a .deb file and parses the output.
    /// </summary>
    private static async Task<List<string>> GetDebContentsAsync(string debPath, string tempDir)
    {
        if (!File.Exists(debPath))
            throw new FileNotFoundException("Deb file not found.", debPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg-deb",
            ArgumentList = { "-c", debPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"dpkg-deb -c failed (exit {process.ExitCode}): {err}");
        }

        return ParseDpkgDebContents(output);
    }
}