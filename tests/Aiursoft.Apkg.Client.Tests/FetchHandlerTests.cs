using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.CommandFramework;
using Aiursoft.CommandFramework.Models;

namespace Aiursoft.Apkg.Client.Tests;

[TestClass]
public class FetchHandlerTests
{
    private static NestedCommandApp App => new NestedCommandApp()
        .WithGlobalOptions(CommonOptionsProvider.VerboseOption)
        .WithFeature(new FetchHandler());

    [TestMethod]
    public async Task HelpDescribesExplicitInputs()
    {
        var result = await App.TestRunAsync(["fetch", "--help"]);
        Assert.AreEqual(0, result.ProgramReturn);
        foreach (var option in new[] { "--repository", "--sources", "--suite", "--arch", "--component", "--signed-by", "--output", "--extract", "--path", "--package" })
            Assert.IsTrue(result.StdOut.Contains(option), option);
    }

    [TestMethod]
    public async Task MissingOptionsAndAmbiguousModesFailWithoutCreatingOutput()
    {
        var output = Path.Combine(Path.GetTempPath(), "apkg-fetch-cli-" + Guid.NewGuid().ToString("N"));
        var cases = new string[][]
        {
            ["fetch"],
            ["fetch", "--package", "grub-common", "--arch", "arm64", "--output", output],
            ["fetch", "--package", "grub-common", "--repository", "https://repo.invalid", "--suite", "base", "--arch", "arm64", "--output", output],
            ["fetch", "--package", "grub-common", "--repository", "https://repo.invalid", "--suite", "base", "--arch", "arm64", "--signed-by", "key.gpg", "--output", output],
            ["fetch", "--path", ".", "--package", "grub-common", "--arch", "arm64", "--output", output],
            ["fetch", "--path", ".", "--suite", "base", "--suite", "updates", "--arch", "arm64", "--output", output],
            ["fetch", "--path", ".", "--suite", "base", "--arch", "arm64", "--output", output],
            ["fetch", "--package", "grub-common", "--sources", "ubuntu.sources", "--repository", "https://repo.invalid", "--arch", "arm64", "--output", output]
        };
        foreach (var args in cases)
        {
            var result = await App.TestRunAsync(args);
            Assert.AreNotEqual(0, result.ProgramReturn, string.Join(' ', args));
            Assert.IsFalse(Directory.Exists(output));
        }
    }
}
