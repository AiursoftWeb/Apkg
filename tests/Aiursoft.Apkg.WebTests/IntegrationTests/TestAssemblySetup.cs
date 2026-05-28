using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WebTools.Attributes;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Per-class host factory. Each test class gets its own isolated IHost so
/// test classes can run in parallel without sharing database or filesystem state.
/// </summary>
[TestClass]
public class TestAssemblySetup
{
    private static readonly List<IHost> Hosts = [];
    private static readonly SemaphoreSlim HostListLock = new(1, 1);
    // Serialises port allocation through StartAsync to prevent TOCTOU
    // races: GetAvailablePort → AppAsync (configure) → StartAsync (bind).
    private static readonly SemaphoreSlim PortLock = new(1, 1);

    private static string? _gpgPublicKey;
    private static string? _gpgPrivateKey;
    private static string? _gpgFingerprint;
    private static readonly SemaphoreSlim GpgLock = new(1, 1);

    [AssemblyInitialize]
    public static Task AssemblyInit(TestContext _)
    {
        LimitPerMin.GlobalEnabled = false;
        return Task.CompletedTask;
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
        foreach (var host in Hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
        Hosts.Clear();
        HostListLock.Dispose();
        PortLock.Dispose();
        GpgLock.Dispose();
    }

    public static async Task<(IHost Host, int Port)> CreateIsolatedHostAsync()
    {
        var dbName = $"ApkgTest_{Guid.NewGuid():N}";

        // Lock section: allocate port, configure Kestrel, bind. We hold the
        // lock until StartAsync because GetAvailablePort does not reserve —
        // it only queries. StartAsync is where the OS binds the port.
        IHost host;
        int port;
        await PortLock.WaitAsync();
        try
        {
            port = Network.GetAvailablePort();
            host = await AppAsync<Startup>(
            [
                $"--ConnectionStrings:DefaultConnection={dbName}",
                $"--Storage:Path=/tmp/data/{dbName}"
            ], port: port);
            await host.StartAsync();
        }
        finally
        {
            PortLock.Release();
        }

        // Everything below is per-class DB work that runs in parallel.
        await host.UpdateDbAsync<ApkgDbContext>();
        await EnsureGpgKeysAsync(host);
        PreInsertGpgCert(host);
        await host.SeedAsync();

        await HostListLock.WaitAsync();
        try
        {
            Hosts.Add(host);
        }
        finally
        {
            HostListLock.Release();
        }

        return (host, port);
    }

    private static void PreInsertGpgCert(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();
        db.AptCertificates.Add(new AptCertificate
        {
            Name = "anduinos",
            FriendlyName = "Anduinos Official Key",
            PublicKey = _gpgPublicKey!,
            PrivateKey = _gpgPrivateKey!,
            Fingerprint = _gpgFingerprint!
        });
        db.SaveChanges();
    }

    private static async Task EnsureGpgKeysAsync(IHost host)
    {
        if (_gpgPublicKey != null) return;

        await GpgLock.WaitAsync();
        try
        {
            if (_gpgPublicKey != null) return;

            using var scope = host.Services.CreateScope();
            var signingService = scope.ServiceProvider.GetRequiredService<IGpgSigningService>();
            var (pub, priv, fpr) = await signingService.GenerateKeyPairAsync(
                "Apkg Default Certificate <support@aiursoft.com>");
            _gpgPublicKey = pub;
            _gpgPrivateKey = priv;
            _gpgFingerprint = fpr;
        }
        finally
        {
            GpgLock.Release();
        }
    }
}
