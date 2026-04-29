using Aiursoft.Apkg.Entities;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WebTools.Attributes;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Starts a single shared test server for the entire assembly.
/// This avoids the ~1s GPG key-generation cost that was incurred by every test class
/// when each class spun up its own IHost via TestBase's ClassInitialize.
/// </summary>
[TestClass]
public class TestAssemblySetup
{
    internal static IHost? Host;
    internal static int Port;

    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext _)
    {
        LimitPerMin.GlobalEnabled = false;
        Port = Network.GetAvailablePort();
        Host = await AppAsync<Startup>([], port: Port);
        await Host.UpdateDbAsync<ApkgDbContext>();
        await Host.SeedAsync();
        await Host.SeedMirrorsAsync(true); // generates GPG cert once
        await Host.StartAsync();
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
        if (Host == null) return;
        await Host.StopAsync();
        Host.Dispose();
        Host = null;
    }
}
