using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
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
        GpgLock.Dispose();
    }

    public static async Task<(IHost Host, int Port)> CreateIsolatedHostAsync()
    {
        var dbName = $"ApkgTest_{Guid.NewGuid():N}";

        using var reservation = new TestPortReservation();
        var port = reservation.Port;
        var host = await AppAsync<Startup>(
        [
            $"--ConnectionStrings:DefaultConnection={dbName}",
            $"--Storage:Path=/tmp/data/{dbName}",
            $"--Storage:ExportPath=/tmp/export/{dbName}"
        ], port: port);
        try
        {
            reservation.ConfigureServer(host);
            await host.StartAsync();
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
        catch
        {
            await host.DisposeAsync();
            throw;
        }
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
