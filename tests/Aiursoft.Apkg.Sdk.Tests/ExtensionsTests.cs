using Aiursoft.Apkg.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class ExtensionsTests
{
    [TestMethod]
    public void AddApkgLocalTools_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApkgLocalTools();

        var provider = services.BuildServiceProvider();

        Assert.IsNotNull(provider.GetService<Services.ManifestSerializer>());
        Assert.IsNotNull(provider.GetService<Services.DebPackageValidator>());
        Assert.IsNotNull(provider.GetService<Services.SystemInfoProvider>());
        Assert.IsNotNull(provider.GetService<Services.AosprojSerializer>());
        Assert.IsNotNull(provider.GetService<Services.ConditionEvaluator>());
        Assert.IsNotNull(provider.GetService<Services.DebBuilder>());
        Assert.IsNotNull(provider.GetService<Services.AosprojLinter>());
    }

    [TestMethod]
    public void AddApkgLocalTools_RegistersAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddApkgLocalTools();
        var provider = services.BuildServiceProvider();

        var s1 = provider.GetService<Services.AosprojSerializer>();
        var s2 = provider.GetService<Services.AosprojSerializer>();
        Assert.AreSame(s1, s2);
    }

    [TestMethod]
    public void AddApkgPush_RegistersHttpClient()
    {
        var services = new ServiceCollection();
        services.AddApkgPush();

        var provider = services.BuildServiceProvider();
        Assert.IsNotNull(provider.GetService<Services.ApkgPushService>());
    }

    [TestMethod]
    public void AddApkgSource_RegistersHttpClient()
    {
        var services = new ServiceCollection();
        services.AddApkgSource();

        var provider = services.BuildServiceProvider();
        Assert.IsNotNull(provider.GetService<Services.ApkgSourceService>());
    }

    [TestMethod]
    public void GetSdkVersion_ReturnsVersion()
    {
        var version = Extensions.GetSdkVersion();
        Assert.IsNotNull(version);
        Assert.IsTrue(version.Major >= 0);
    }

    [TestMethod]
    public void ServerConfig_DefaultIsEmpty()
    {
        var config = new ServerConfig();
        Assert.AreEqual(string.Empty, config.Instance);
    }
}
