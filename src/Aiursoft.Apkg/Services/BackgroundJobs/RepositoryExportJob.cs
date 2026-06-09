using System.Runtime.InteropServices;
using System.Text;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Canon.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

/// <summary>
/// Periodic background job that materializes all APT repositories as a static file tree
/// under <c>{Storage:ExportPath}</c>.
///
/// <para><b>Purpose:</b> Produce a directory tree that a static file server (nginx / caddy / rsync)
/// can serve directly, with URLs <b>identical</b> to those served by <see cref="Controllers.AptMirrorController"/>.</para>
///
/// <para><b>Directory layout produced:</b></para>
/// <code>
/// {ExportPath}/
///   .stage/                                              ← Staging (hidden, rsync/nginx ignore)
///     artifacts/
///   .prev/                                               ← Previous artifacts backup
///   artifacts/                                           ← Live (served by nginx/caddy)
///     certs/
///       {certName}                                         ← GPG public key (no extension, matches URL)
///     {distro}/
///       dists/
///         {suite}/
///           InRelease                                      ← GPG-signed metadata
///           Release                                        ← Unsigned metadata
///           {component}/
///             binary-{arch}/
///               Packages                                   ← Package index (plain)
///               Packages.gz                                ← Package index (gzip)
///             Contents-{arch}                              ← File-to-package mapping
///             Contents-{arch}.gz                           ← File-to-package mapping (gzip)
///       {suite}/
///         pool/
///           {component}/
///             {firstLetter}/
///               {packageName}/
///                 {packageName}_{version}_{arch}.deb        ← Hardlink to CAS
/// </code>
///
/// <para><b>Atomic swap:</b> Files are written to <c>.stage/artifacts/</c> inside the export path.
/// On success, the live <c>artifacts/</c> directory is atomically swapped with <c>.stage/</c>
/// via <c>rename(2)</c>. Because both directories reside on the same filesystem (even under
/// Docker bind mounts), the swap is atomic and never exposes half-written state to APT
/// clients or rsync.</para>
///
/// <para><b>Pool files:</b> .deb files are hardlinked from CAS (<c>Objects/{sha256[..2]}/{sha256}.deb</c>)
/// to the pool path. This avoids duplicating multi-GB of binaries. Hardlinks are real files
/// on the same filesystem, so rsync/nginx/caddy treat them natively. Cross-filesystem fallback copies.</para>
/// </summary>
public class RepositoryExportJob(
    ApkgDbContext db,
    FeatureFoldersProvider folders,
    IConfiguration configuration,
    ILogger<RepositoryExportJob> logger) : IBackgroundJob
{
    public string Name => "Export APT repositories as static files";

    public string Description =>
        "Materializes all APT repositories into a static file tree under the configured ExportPath. " +
        "The output can be served by nginx/caddy or synced to edge nodes via rsync. " +
        "Uses atomic directory swap to prevent serving half-written state.";

    public async Task ExecuteAsync()
    {
        var exportRoot = configuration["Storage:ExportPath"];
        if (string.IsNullOrWhiteSpace(exportRoot))
        {
            logger.LogInformation("Storage:ExportPath is not configured. Skipping repository export.");
            return;
        }

        logger.LogInformation("RepositoryExportJob started. ExportPath: {ExportPath}", exportRoot);

        var cleanRoot = exportRoot.TrimEnd('/');
        var stageDir = Path.Combine(cleanRoot, ".stage");
        var prevDir = Path.Combine(cleanRoot, ".prev");
        var liveDir = Path.Combine(cleanRoot, "artifacts");

        try
        {
            // Clean up any leftover staging directory from a previous failed run
            if (Directory.Exists(stageDir))
            {
                Directory.Delete(stageDir, recursive: true);
            }

            Directory.CreateDirectory(cleanRoot);
            Directory.CreateDirectory(stageDir);

            await ExportCertificatesAsync(stageDir);
            var anyRepoFailed = await ExportRepositoriesAsync(stageDir);

            if (anyRepoFailed)
            {
                logger.LogWarning(
                    "One or more repositories failed to export. Abandoning this build. " +
                    "The previous live export will continue to be served.");
                // Clean up staging on partial failure
                if (Directory.Exists(stageDir))
                    Directory.Delete(stageDir, recursive: true);
                return;
            }

            // Atomic swap: .stage/artifacts → live artifacts, old artifacts → .prev
            var stageArtifactsDir = Path.Combine(stageDir, "artifacts");
            if (Directory.Exists(stageArtifactsDir))
            {
                AtomicSwap(liveDir, stageArtifactsDir, prevDir);
                // Clean up the now-empty .stage directory
                if (Directory.Exists(stageDir))
                    Directory.Delete(stageDir);
            }
            else
            {
                logger.LogInformation("No artifacts were generated in this export cycle. No swap performed.");
                if (Directory.Exists(stageDir))
                    Directory.Delete(stageDir);
            }

            logger.LogInformation("RepositoryExportJob finished successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RepositoryExportJob failed.");

            // Clean up staging on failure
            try
            {
                if (Directory.Exists(stageDir))
                    Directory.Delete(stageDir, recursive: true);
            }
            catch
            {
                // best-effort
            }

            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Certificates
    // ═══════════════════════════════════════════════════════════════════════

    private async Task ExportCertificatesAsync(string stageDir)
    {
        var certs = await db.AptCertificates
            .AsNoTracking()
            .ToListAsync();

        if (certs.Count == 0) return;

        var certsDir = Path.Combine(stageDir, "artifacts", "certs");
        Directory.CreateDirectory(certsDir);

        foreach (var cert in certs)
        {
            // The controller route is /artifacts/certs/{name} (no extension).
            // The .asc only appears in Content-Disposition header, not the URL.
            var certPath = Path.Combine(certsDir, cert.Name);
            await File.WriteAllTextAsync(certPath, cert.PublicKey, Encoding.UTF8);
            logger.LogDebug("Exported certificate: {CertName}", cert.Name);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Repositories
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<bool> ExportRepositoriesAsync(string stageDir)
    {
        var repos = await db.AptRepositories
            .AsNoTracking()
            .Include(r => r.PrimaryBucket)
            .ToListAsync();

        var anyFailed = false;

        foreach (var repo in repos)
        {
            if (repo.PrimaryBucket == null)
            {
                logger.LogDebug("Repository {RepoName} has no primary bucket. Skipping.", repo.Name);
                continue;
            }

            try
            {
                await ExportSingleRepositoryAsync(stageDir, repo, repo.PrimaryBucket);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to export repository {RepoName}", repo.Name);
                anyFailed = true;
            }
        }

        return anyFailed;
    }

    private async Task ExportSingleRepositoryAsync(string stageDir, AptRepository repo, AptBucket bucket)
    {
        logger.LogInformation("Exporting repository {RepoName} (Distro={Distro}, Suite={Suite}, BucketId={BucketId})...",
            repo.Name, repo.Distro, repo.Suite, bucket.Id);

        var bucketsRoot = folders.GetBucketsFolder();

        // ── dists metadata ──────────────────────────────────────────────
        // Route: artifacts/{distro}/dists/{suite}/...
        var distsBase = Path.Combine(stageDir, "artifacts", repo.Distro, "dists", repo.Suite);
        Directory.CreateDirectory(distsBase);

        // InRelease
        if (!string.IsNullOrEmpty(bucket.InReleaseContent))
        {
            await File.WriteAllTextAsync(
                Path.Combine(distsBase, "InRelease"),
                bucket.InReleaseContent,
                Encoding.UTF8);
        }

        // Release
        if (!string.IsNullOrEmpty(bucket.ReleaseContent))
        {
            await File.WriteAllTextAsync(
                Path.Combine(distsBase, "Release"),
                bucket.ReleaseContent,
                Encoding.UTF8);
        }

        // Packages, Packages.gz, Contents-{arch}, Contents-{arch}.gz
        // These are already written to disk by RepositorySyncJob at:
        //   Buckets/{bucketId}/{component}/binary-{arch}/Packages[.gz]
        //   Buckets/{bucketId}/{component}/Contents-{arch}[.gz]
        var bucketDir = Path.Combine(bucketsRoot, bucket.Id.ToString());

        var architectures = repo.Architecture.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var components = repo.Components.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var component in components)
        {
            foreach (var arch in architectures)
            {
                // binary-{arch}/Packages[.gz]
                var srcPkgDir = Path.Combine(bucketDir, component, $"binary-{arch}");
                var dstPkgDir = Path.Combine(distsBase, component, $"binary-{arch}");
                Directory.CreateDirectory(dstPkgDir);

                CopyIfExists(Path.Combine(srcPkgDir, "Packages"), Path.Combine(dstPkgDir, "Packages"));
                CopyIfExists(Path.Combine(srcPkgDir, "Packages.gz"), Path.Combine(dstPkgDir, "Packages.gz"));

                // Contents-{arch}[.gz]
                var srcContentsDir = Path.Combine(bucketDir, component);
                CopyIfExists(Path.Combine(srcContentsDir, $"Contents-{arch}"), Path.Combine(distsBase, component, $"Contents-{arch}"));
                CopyIfExists(Path.Combine(srcContentsDir, $"Contents-{arch}.gz"), Path.Combine(distsBase, component, $"Contents-{arch}.gz"));
            }
        }

        // ── pool files (hardlinks from CAS) ────────────────────────────
        // The Packages file has Filename entries rewritten by RepositorySyncJob as:
        //   {suite}/pool/{component}/{firstLetter}/{packageName}/{fileName}
        //
        // APT clients read the rewritten Filename and construct URLs of the form:
        //   artifacts/{distro}/{suite}/pool/{**path}  → GetSuitePool
        //
        // We materialize this suite-scoped path:
        //   artifacts/{distro}/{suite}/pool/...
        await ExportPoolFilesAsync(stageDir, repo, bucket);

        logger.LogInformation("Repository {RepoName} exported successfully.", repo.Name);
    }

    private async Task ExportPoolFilesAsync(string stageDir, AptRepository repo, AptBucket bucket)
    {
        var objectsRoot = folders.GetObjectsFolder();

        // Stream through all packages in this bucket to create pool symlinks
        var packages = await db.AptPackages
            .AsNoTracking()
            .Where(p => p.BucketId == bucket.Id)
            .Select(p => new { p.Filename, p.SHA256 })
            .ToListAsync();

        foreach (var pkg in packages)
        {
            if (string.IsNullOrWhiteSpace(pkg.Filename) || string.IsNullOrWhiteSpace(pkg.SHA256))
                continue;

            var hash = pkg.SHA256.ToLowerInvariant();
            var hashPrefix = hash[..2];
            var casPath = Path.Combine(objectsRoot, hashPrefix, $"{hash}.deb");

            // The Filename field stored in DB is: pool/{component}/{firstLetter}/{pkg}/{...}.deb
            // (no suite prefix — the suite prefix is only injected into Packages.gz output).
            // APT clients read the rewritten Filename from Packages.gz and request URLs of the
            // form artifacts/{distro}/{suite}/pool/..., so we only need suite-scoped paths.
            var filename = pkg.Filename.TrimStart('/');

            // artifacts/{distro}/{suite}/pool/...
            var suitePoolPath = Path.Combine(stageDir, "artifacts", repo.Distro, repo.Suite, filename);
            LinkDebFile(casPath, suitePoolPath);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private void CopyIfExists(string src, string dst)
    {
        if (!File.Exists(src))
        {
            logger.LogDebug("Source file does not exist, skipping: {Src}", src);
            return;
        }

        var dstDir = Path.GetDirectoryName(dst);
        if (dstDir != null && !Directory.Exists(dstDir))
            Directory.CreateDirectory(dstDir);

        File.Copy(src, dst, overwrite: true);
    }

    private void LinkDebFile(string target, string link, bool skipIfExists = false)
    {
        if (!File.Exists(target))
        {
            logger.LogDebug("CAS file does not exist, skipping: {Target}", target);
            return;
        }

        if (skipIfExists && File.Exists(link))
            return;

        var linkDir = Path.GetDirectoryName(link);
        if (linkDir != null && !Directory.Exists(linkDir))
            Directory.CreateDirectory(linkDir);

        // Remove existing file at the link path
        if (File.Exists(link))
            File.Delete(link);

        try
        {
            // Hardlink saves disk space and works everywhere (nginx, rsync, caddy, CDN).
            CreateHardLink(link, target);
        }
        catch (IOException)
        {
            // Cross-filesystem fallback.  This doubles disk usage for the file
            // but is the safe alternative when export and CAS are on different volumes.
            logger.LogWarning(
                "Hardlink failed (likely cross-filesystem). Falling back to copy for: {Link}", link);
            File.Copy(target, link, overwrite: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hardlink failed with unexpected error. Falling back to copy for: {Link}", link);
            File.Copy(target, link, overwrite: false);
        }
    }

    /// <summary>
    /// Creates a hard link via the POSIX <c>link(2)</c> syscall on Linux/macOS,
    /// or <c>CreateHardLinkW</c> on Windows.
    /// </summary>
    private static void CreateHardLink(string link, string target)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: use kernel32 CreateHardLinkW via P/Invoke
            if (!CreateHardLinkW(link, target, IntPtr.Zero))
            {
                var err = Marshal.GetLastWin32Error();
                throw new IOException($"CreateHardLinkW failed: {err}");
            }
        }
        else
        {
            // POSIX: link(oldpath, newpath)
            if (link_C(target, link) != 0)
            {
                var err = Marshal.GetLastPInvokeError();
                throw new IOException($"link(2) failed: {err}");
            }
        }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int link_C(string oldpath, string newpath);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    /// <summary>
    /// Atomically swaps the staging directory into the live export path.
    /// <list type="number">
    ///   <item>If <paramref name="prevDir"/> exists, delete it.</item>
    ///   <item>If <paramref name="liveDir"/> exists, rename it to <paramref name="prevDir"/>.</item>
    ///   <item>Rename <paramref name="stageDir"/> to <paramref name="liveDir"/>.</item>
    /// </list>
    /// </summary>
    private void AtomicSwap(string liveDir, string stageDir, string prevDir)
    {
        logger.LogInformation("Performing atomic swap: {Stage} → {Live}", stageDir, liveDir);

        if (Directory.Exists(prevDir))
        {
            Directory.Delete(prevDir, recursive: true);
        }

        if (Directory.Exists(liveDir))
        {
            Directory.Move(liveDir, prevDir);
        }

        Directory.Move(stageDir, liveDir);
    }
}
