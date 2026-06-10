namespace Aiursoft.AptClient.Tests;
using Aiursoft.AptClient;
using Aiursoft.AptClient.Abstractions;

[TestClass]
public class TestMirrorDirect
{
    [TestMethod]
    public async Task DebugFetchFromRealMirror()
    {
        var repo = new AptRepository("https://mirror.aiursoft.com/ubuntu/", "noble", signedBy: null, allowInsecure: true);
        await repo.EnsureVerifiedAsync();
        var files = (await repo.GetSupportedFilesAsync()).ToList();
        Console.WriteLine($"Supported files: {files.Count}");
        foreach (var f in files.Where(f => f.Contains("Packages.gz")).Take(5))
            Console.WriteLine($"  {f}");
        
        var source = new AptPackageSource(repo, "main", "amd64");
        int count = 0;
        await foreach (var pkg in source.FetchPackagesAsync())
        {
            if (pkg.Package.Package == "base-files")
                Console.WriteLine($"FOUND base-files: {pkg.Package.Version} arch={pkg.Package.Architecture} file={pkg.Package.Filename}");
            count++;
        }
        Console.WriteLine($"Total packages: {count}");
        Assert.IsTrue(count > 0, "Should find at least some packages");
    }
}
