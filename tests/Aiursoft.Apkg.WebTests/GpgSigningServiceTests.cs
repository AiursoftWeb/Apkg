using Aiursoft.Apkg.Services.Authentication;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class GpgSigningServiceTests
{
    private IGpgSigningService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IGpgSigningService, GpgSigningService>();
        var provider = services.BuildServiceProvider();
        _service = provider.GetRequiredService<IGpgSigningService>();
    }

    [TestMethod]
    public async Task TestGenerateAndSign()
    {
        // 1. Test Key Generation
        var (pub, priv, fpr) = await _service.GenerateKeyPairAsync("Test User <test@example.com>");

        Assert.IsFalse(string.IsNullOrWhiteSpace(pub));
        Assert.IsFalse(string.IsNullOrWhiteSpace(priv));
        Assert.IsFalse(string.IsNullOrWhiteSpace(fpr));
        Assert.IsTrue(pub.Contains("BEGIN PGP PUBLIC KEY BLOCK"));
        Assert.IsTrue(priv.Contains("BEGIN PGP PRIVATE KEY BLOCK"));

        // 2. Test Signing
        const string message = "Hello APT World!";
        var signed = await _service.SignClearsignAsync(message, priv);

        Assert.IsFalse(string.IsNullOrWhiteSpace(signed));
        Assert.IsTrue(signed.Contains("BEGIN PGP SIGNED MESSAGE"));
        Assert.IsTrue(signed.Contains(message));
        Assert.IsTrue(signed.Contains("BEGIN PGP SIGNATURE"));
    }
}
