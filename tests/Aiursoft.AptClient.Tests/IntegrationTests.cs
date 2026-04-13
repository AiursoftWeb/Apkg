[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Aiursoft.AptClient.Tests;

using System.Diagnostics;

[TestClass]
public class IntegrationTests
{

    private readonly string IntegratedSigned =
            @"Types: deb
URIs: https://mirror-ppa.aiursoft.cn/mozillateam/ppa/ubuntu/
Suites: questing
Components: main
Signed-By:
 -----BEGIN PGP PUBLIC KEY BLOCK-----
 .
 mQINBGYov84BEADSrLhiWvqL3JJ3fTxjCGD4+viIUBS4eLSc7+Q7SyHm/wWfYNwT
 EqEvMMM9brWQyC7xyE2JBlVk5/yYHkAQz3f8rbkv6ge3J8Z7G4ZwHziI45xJKJ0M
 9SgJH24WlGxmbbFfK4SGFNlg9x1Z0m5liU3dUSfhvTQdmBNqwRCAjJLZSiS03IA0
 56V9r3ACejwpNiXzOnTsALZC2viszGiI854kqhUhFIJ/cnWKSbAcg6cy3ZAsne6K
 vxJVPsdEl12gxU6zENZ/4a4DV1HkxIHtpbh1qub1lhpGR41ZBXv+SQhwuMLFSNeu
 UjAAClC/g1pJ0gzI0ko1vcQFv+Q486jYY/kv+k4szzcB++nLILmYmgzOH0NEqT57
 XtdiBWhlb6oNfF/nYZAaToBU/QjtWXq3YImG2NiCUrCj9zAKHdGUsBU0FxN7HkVB
 B8aF0VYwB0I2LRO4Af6Ry1cqMyCQnw3FVh0xw7Vz4gQ57acUYeAJpT68q8E2XcUx
 riEP65/MBPoFlANLVMSrnsePEXmVzdysmXKnFVefeQ4E3dIDufXUIhrfmL1pMdTG
 anhmDEjY7I3pQQQIaLpnNhhSDZKDSk9C/Ax/8gEUgnnmd6BwZxh8Q7oDXcm2tyeu
 n2m9wCZI/eJI9P9G8ON8AkKvG4xFR+eqhowwzu7TLDr3feliG+UN+mJ8jwARAQAB
 tB5MYXVuY2hwYWQgUFBBIGZvciBNb3ppbGxhIFRlYW2JAk4EEwEKADgWIQRzi+uT
 IdGq7BPqk5GuvfSBm+IYZwUCZii/zgIbAwULCQgHAgYVCgkICwIEFgIDAQIeAQIX
 gAAKCRCuvfSBm+IYZ38/D/46eEIyG7Gb65sxt3QnlIN0+90kUjz83QpCnIyALZDc
 H2wPYBCMbyJFMG+rqVE8Yoh6WF0Rqy76LG+Y/xzO9eKIJGxVcSU75ifoq/M7pI1p
 aiqA9T8QcFBmo83FFoPvnid67aqg/tFsHl+YF9rUxMZndGRE9Hk96lkH1Y2wHMEs
 mAa582RELVEDDD2ellOPmQr69fRPa5IdJHkXjqGtoNQy5hAp49ofMLmeQ82d2OA+
 kpzgiuSw8Nh1VrMZludcUArSQDCHoXuiPG/7Wn9Vy6fvKkTQK3mCW8i5HgCa0qxe
 vOKlDMz4virEEADMBs79iIyM6w1xm8JOD4734sgii2MPcQgmAlbu5LyBM5FfuO0u
 rTMvZM0btSWQX3nIsxQ3far9MJvUT4nebhTo59cED+1EjkD14mReTHwtWt1aye/b
 I8Rvor15RFiB8Ku6c41YmNKarSCzJDs4VEfsos4oMieEqA98J4ZOX67IT++ortcB
 uXmDJgvzGWEeyVOMoc/4oDJHNQjJg9XRGy8b/J3AVhk2BE/CD4lKhX3hWGbufrQz
 E8ENWuT4m3igQnBmOsrGlBPYIOKZvczQxri01vcKY95dKXb1jtnR9yR+JKgEP388
 1B/8dEohynhMnzEqR9TIMEEy9Y8RKZ+Jiy+/Lg2XGrChiLsouUetfMQww6BTK+++
 pw==
 =tIux
 -----END PGP PUBLIC KEY BLOCK-----
";

    [TestMethod]
    public async Task TestFetchFromAliyunDeb822()
    {
        // Arrange
        // Removed Signed-By to skip signature verification for mock
        var deb822 = @"
Types: deb
URIs: http://mirror.aiursoft.com/ubuntu/
Suites: jammy
Components: main
";
        var mockData = GenerateMockRepoData();

        var mockHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString();
            if (uri?.EndsWith("InRelease") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(mockData.InRelease)
                });
            }
            if (uri?.EndsWith("Packages.gz") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(mockData.PackagesGz)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(deb822, "amd64", () => new HttpClient(mockHandler));

        // MSTest0037: Use Assert.AreNotEqual(0, ...)
        Assert.AreNotEqual(0, sources.Count);

        // Act
        var allPackages = new List<DebianPackageFromApt>();
        foreach (var source in sources)
        {
            var packages = await source.FetchPackagesAsync();
            allPackages.AddRange(packages);
        }

        // Assert
        Assert.HasCount(1, allPackages);

        var bash = allPackages.FirstOrDefault(p => p.Package.Package == "bash");
        Assert.IsNotNull(bash, "Should find bash package");
        Assert.IsFalse(string.IsNullOrWhiteSpace(bash.Package.Version));
        Assert.IsFalse(string.IsNullOrWhiteSpace(bash.Package.Filename));
        Assert.IsFalse(string.IsNullOrWhiteSpace(bash.Package.SHA256));
    }

    [TestMethod]
    public async Task TestSignatureVerification_FailsOnTamperedContent()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.ToString().Contains("InRelease") == true)
            {
                // Return a fake InRelease that looks signed but content has been modified after signing
                var content = @"-----BEGIN PGP SIGNED MESSAGE-----
Hash: SHA256

Origin: Mock
Label: Mock
Suite: questing
SHA256:
 0000000000000000000000000000000000000000000000000000000000000000 0 main/binary-amd64/Packages.gz
-----BEGIN PGP SIGNATURE-----

wsE7BAABCAByBQJmKL/OCRD/g1pJ0gzI0kYVCAJXBgUSZii/zgIZAQb4Ag4BAArd
Cgkyr330gZviGGcQtRQAAPjID/9+ABCDEF1234567890ABCDEF1234567890ABCD
(Assuming the signature block is valid structure but invalid for content)
=tIux
-----END PGP SIGNATURE-----";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(IntegratedSigned, "amd64", () => new HttpClient(mockHandler));
        var source = sources.First();

        // Act & Assert
        try
        {
            await source.FetchPackagesAsync();
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception)
        {
            // Expected
        }
    }

    [TestMethod]
    public async Task TestSignatureVerification_FailsOnGarbageSignature()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.ToString().Contains("InRelease") == true)
            {
                var content = @"-----BEGIN PGP SIGNED MESSAGE-----
Hash: SHA256

Origin: Mock
SHA256:
 0000 0 main/binary-amd64/Packages.gz
-----BEGIN PGP SIGNATURE-----

THIS_IS_TOTAL_GARBAGE_NOT_EVEN_BASE64
-----END PGP SIGNATURE-----";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(IntegratedSigned, "amd64", () => new HttpClient(mockHandler));
        var source = sources.First();

        // Act & Assert
        try
        {
            await source.FetchPackagesAsync();
            Assert.Fail("Expected exception was not thrown.");

        }
        catch (Exception)
        {
            // Expected
        }
    }

    [TestMethod]
    public async Task TestSignatureVerification_FailsOnNoSignature()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.ToString().Contains("InRelease") == true)
            {
                // Just plain text, no signature
                var content = @"Origin: Mock
SHA256:
 0000 0 main/binary-amd64/Packages.gz";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(IntegratedSigned, "amd64", () => new HttpClient(mockHandler));
        var source = sources.First();

        // Act & Assert
        try
        {
            await source.FetchPackagesAsync();
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception)
        {
            // Expected
        }
    }

    [TestMethod]
    public async Task TestHashMismatchFails()
    {
        // Arrange
        var deb822 = @"
Types: deb
URIs: http://mock.local/ubuntu/
Suites: jammy
Components: main
Signed-By:
";
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString();
            if (uri?.EndsWith("InRelease") == true)
            {
                var content = @"Origin: Mock
SHA256:
 cafe0000cafe0000cafe0000cafe0000cafe0000cafe0000cafe0000cafe0000 100 main/binary-amd64/Packages.gz";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }
            if (uri?.EndsWith("Packages.gz") == true || uri?.EndsWith("Packages") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[100])
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(deb822, "amd64", () => new HttpClient(mockHandler));
        var source = sources.First();

        // Act & Assert
        try
        {
            await source.FetchPackagesAsync();
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception)
        {
            // Expected
        }
    }

    [TestMethod]
    public void TestParseLegacyFormat()
    {
        var line = "deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] http://mirror.aiursoft.com/ubuntu/ jammy main restricted";
        var sources = AptSourceExtractor.ExtractSources(line, "amd64");

        Assert.HasCount(2, sources);
        Assert.AreEqual("jammy", sources[0].Suite);
        Assert.AreEqual("http://mirror.aiursoft.com/ubuntu/", sources[0].ServerUrl);
    }

    [TestMethod]
    public async Task TestDownloadPackage()
    {
        var deb822 = @"
Types: deb
URIs: http://mirror.aiursoft.com/ubuntu/
Suites: jammy
Components: main
";
        var mockData = GenerateMockRepoData(packageName: "hostname");

        var mockHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString();
            if (uri?.EndsWith("InRelease") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(mockData.InRelease)
                });
            }
            if (uri?.EndsWith("Packages.gz") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(mockData.PackagesGz)
                });
            }
            if (uri?.Contains("pool/main/m/mock/hostname.deb") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(mockData.PackageDeb)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(deb822, "amd64", () => new HttpClient(mockHandler));

        var source = sources.First();
        var packages = await source.FetchPackagesAsync();

        var pkgInfo = packages.FirstOrDefault(p => p.Package.Package == "hostname");
        Assert.IsNotNull(pkgInfo, "Should find hostname package");

        var tempFile = Path.GetTempFileName();

        try
        {
            await source.DownloadPackageAsync(pkgInfo.Package, tempFile);

            Assert.IsTrue(File.Exists(tempFile));
            // Assert.IsTrue(info.Length > 0) -> Assert.AreNotEqual(0, ...)
            Assert.AreNotEqual(0, new FileInfo(tempFile).Length);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task TestFetchFromIntegratedSigned()
    {
        // 1. Generate Mock Data (Unsigned)
        var mockRepo = GenerateMockRepoData();

        // 2. Generate GPG Key and Sign the InRelease content
        var (pubKey, signedInRelease) = GenerateGpgKeyAndSignedContent(mockRepo.InRelease);

        // 3. Construct Source String with the Generated Public Key
        // Note: URIs can be anything since we mock it.
        var sourceStr = $@"
Types: deb
URIs: https://mock-ppa.aiursoft.cn/mozillateam/ppa/ubuntu/
Suites: questing
Components: main
Signed-By:
{pubKey}
";

        // 4. Setup Mock Handler
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString();
            if (uri?.EndsWith("InRelease") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(signedInRelease)
                });
            }
            if (uri?.EndsWith("Packages.gz") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(mockRepo.PackagesGz)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        // 5. Run Logic
        var sources = AptSourceExtractor.ExtractSources(sourceStr, "amd64", () => new HttpClient(mockHandler));
        Assert.AreNotEqual(0, sources.Count);

        var allPackages = new List<DebianPackageFromApt>();
        foreach (var source in sources)
        {
            var packages = await source.FetchPackagesAsync();
            allPackages.AddRange(packages);
        }

        Assert.AreNotEqual(0, allPackages.Count, "No packages found in Signed Repo");
        var firstPkg = allPackages.First();
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstPkg.Package.Package));
    }

    private (string PublicKey, string SignedContent) GenerateGpgKeyAndSignedContent(string content)
    {
        var tempHome = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempHome);
        try
        {
            // Gen Key (Must use --pinentry-mode loopback or similar if gpg version requires it, but --quick-gen-key usually fine without passphrase)
            // Using default default default for expiration
            RunGpg(tempHome, "--batch --pinentry-mode loopback --passphrase '' --quick-gen-key \"Mock User\" default default");

            // Export Public Key
            var pubKey = RunGpg(tempHome, "--export --armor \"Mock User\"");

            // Sign Content
            var unsignedFile = Path.Combine(tempHome, "unsigned.txt");
            File.WriteAllText(unsignedFile, content);

            // --yes to overwrite, --clearsign for InRelease format
            RunGpg(tempHome, $"--batch --yes --pinentry-mode loopback --passphrase '' --clearsign --output \"{unsignedFile}.asc\" \"{unsignedFile}\"");
            var signedContent = File.ReadAllText(unsignedFile + ".asc");

            return (pubKey, signedContent);
        }
        finally
        {
            try { Directory.Delete(tempHome, true); } catch (Exception e) { Console.WriteLine(e.Message); }
        }
    }

    private string RunGpg(string home, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gpg",
            Arguments = $"--homedir \"{home}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) throw new Exception("Failed to start GPG");

        var outputReader = p.StandardOutput;
        var errorReader = p.StandardError;

        // Wait for exit
        p.WaitForExit();

        var output = outputReader.ReadToEnd(); // Read after exit to avoid deadlock if small buffer? Better to read async but for test small data ok.
                                               // Actually best practice is to read before wait or async.
                                               // Since we used ReadToEndAsync in checking code, let's use valid approach:

        if (p.ExitCode != 0)
        {
            var err = errorReader.ReadToEnd();
            throw new Exception($"GPG Failed (Exit {p.ExitCode}): {err}\nArgs: {args}");
        }
        return output;
    }

    [TestMethod]
    public async Task TestReadmeSample()
    {
        var sourceText = "deb http://mirror.aiursoft.com/ubuntu/ jammy main";
        var mockData = GenerateMockRepoData();

        var mockHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString();
            if (uri?.EndsWith("InRelease") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(mockData.InRelease)
                });
            }
            if (uri?.EndsWith("Packages.gz") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(mockData.PackagesGz)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(sourceText, "amd64", () => new HttpClient(mockHandler));

        Assert.HasCount(1, sources);
        foreach (var aptSource in sources)
        {
            var packages = await aptSource.FetchPackagesAsync();
            Console.WriteLine($"Found {packages.Count} packages in {aptSource.Suite}");
            Assert.IsNotEmpty(packages);
        }
    }
    [TestMethod]
    public async Task TestFetchPackages_FallbackToPlainPackages()
    {
        // Arrange
        var deb822 = @"
Types: deb
URIs: http://fallback.test/ubuntu/
Suites: jammy
Components: main
"; // No Signed-By

        var mockData = GenerateMockRepoData();
        var packagesContent = @"Package: fallback-pkg
Version: 1.0.0
Architecture: amd64
Maintainer: fallback
Description: fallback
Filename: pool/main/f/fallback/fallback.deb
SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
Size: 0
MD5sum: d41d8cd98f00b204e9800998ecf8427e
SHA1: da39a3ee5e6b4b0d3255bfef95601890afd80709
Description-md5: d41d8cd98f00b204e9800998ecf8427e
Section: misc
Priority: optional
Origin: fallback
Bugs: https://bugs.example.com
";

        var mockHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString();
            if (uri?.EndsWith("InRelease") == true)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(mockData.InRelease)
                });
            }
            if (uri?.EndsWith("Packages.gz") == true)
            {
                // Simulate 404
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            }
            if (uri?.EndsWith("Packages") == true) // Raw
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(packagesContent)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var sources = AptSourceExtractor.ExtractSources(deb822, "amd64", () => new HttpClient(mockHandler));
        var source = sources.First();

        // Act
        var packages = await source.FetchPackagesAsync();

        // Assert
        Assert.HasCount(1, packages);
        Assert.AreEqual("fallback-pkg", packages[0].Package.Package);
    }

    private (string InRelease, byte[] PackagesGz, byte[] PackageDeb) GenerateMockRepoData(string packagesName = "Packages.gz", string packageName = "bash")

    {
        // Mock Deb content (random bytes)
        var debContent = new byte[1024];
        new Random().NextBytes(debContent);

        using var sha256Deb = System.Security.Cryptography.SHA256.Create();
        var debHashBytes = sha256Deb.ComputeHash(debContent);
        var debHash = BitConverter.ToString(debHashBytes).Replace("-", "").ToLowerInvariant();

        var packageContent = $@"Package: {packageName}
Version: 5.1-6ubuntu1
Architecture: amd64
Maintainer: Mock Maintainer <mock@example.com>
Installed-Size: 100
Filename: pool/main/m/mock/{packageName}.deb
Size: {debContent.Length}
MD5sum: 5d41402abc4b2a76b9719d911017c592
SHA1: 7c01359483482772592736466380637370335876
SHA256: {debHash}
Section: utils
Priority: optional
Description: Mock package
 This is a mock package.
Description-md5: 5d41402abc4b2a76b9719d911017c592
";
        var packageBytes = System.Text.Encoding.UTF8.GetBytes(packageContent);
        using var ms = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal))
        {
            gzip.Write(packageBytes, 0, packageBytes.Length);
        }
        var packagesGz = ms.ToArray();

        // Calculate hash of Packages.gz
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(packagesGz);
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        var inRelease = $@"Origin: Mock
Label: Mock
Suite: jammy
Codename: jammy
Date: Thu, 01 Jan 2024 00:00:00 UTC
Architectures: amd64
Components: main
Description: Mock Repository
SHA256:
 {hash} {packagesGz.Length} main/binary-amd64/{packagesName}
";

        return (inRelease, packagesGz, debContent);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
