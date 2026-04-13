

using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.AptClient.SampleApp;

[ExcludeFromCodeCoverage]
class Program
{
    private static int _getCounter = 1;

    static async Task Main()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 模拟多个不同的 sources 文件内容
        var fileContents = new List<string>
        {
            // 1. 标准 deb822
            @"
Types: deb
URIs: http://mirrors.aliyun.com/ubuntu/
Suites: questing
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg
",
            // 2. Legacy .list 格式
            // 注意：这里为了演示使用 aliyun，通常 list 格式也是配置 aliyun
            "deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] http://mirrors.aliyun.com/ubuntu/ questing-updates main restricted",

            // 3. PPA 格式
            // 注意：这里为了演示使用 aliyun，通常 PPA 格式也是配置 aliyun
            @"Types: deb
URIs: https://mirror-ppa.aiursoft.com/mozillateam/ppa/ubuntu/
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
 "
        };

        var allSources = new List<AptPackageSource>();
        foreach (var content in fileContents)
        {
            allSources.AddRange(AptSourceExtractor.ExtractSources(content, "amd64"));
        }

        Console.WriteLine($"Extracted {allSources.Count} sources from {fileContents.Count} configs.");
        long totalBytes = 0;

        Action<string, long> onProgress = (url, size) =>
        {
            // Progress logic...
            totalBytes += size;
            var sizeStr = FormatBytes(size);

            // Try to extract useful "apt-like" info
            // url: http://.../dists/questing/InRelease -> questing InRelease
            // url: http://.../dists/questing/main/binary-amd64/Packages.gz -> questing/main amd64 Packages

            var display = url;
            if (url.Contains("/dists/"))
            {
                // ... same display logic
                var part = url.Substring(url.IndexOf("/dists/", StringComparison.Ordinal) + 7); // questing/InRelease or questing/main/binary-amd64/Packages.gz
                var parts = part.Split('/');
                if (parts.Length >= 2 && parts.Last() == "InRelease")
                {
                    // questing InRelease
                    display = $"{parts[0]} InRelease";
                }
                else if (parts.Length >= 4 && parts.Last().StartsWith("Packages"))
                {
                    // part: questing/main/binary-amd64/Packages.gz
                    // parts[0] = questing (suite)
                    // parts[1] = main (component)
                    // parts[2] = binary-amd64
                    var suite = parts[0];
                    var comp = parts[1];
                    var arch = parts[2].Replace("binary-", "");
                    display = $"{suite}/{comp} {arch} Packages";
                }
            }

            // Clean up base url for display if possible, or just use computed display
            // Apt output: Get:1 http://mirrors... questing/main amd64 Packages
            // Let's just output the URL base + valid info

            var baseUri = new Uri(url);
            var host = $"{baseUri.Scheme}://{baseUri.Host}";

            Console.WriteLine($"Get:{_getCounter++} {host} {display} [{sizeStr}]");
        };

        var allPackages = new List<DebianPackageFromApt>();

        foreach (var source in allSources)
        {
            try
            {
                var packages = await source.FetchPackagesAsync(onProgress);
                allPackages.AddRange(packages);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Err:{_getCounter++} {source.ServerUrl} {source.Suite} Error: {ex.Message}");
            }
        }

        sw.Stop();

        var totalSizeStr = FormatBytes(totalBytes);
        var totalSeconds = sw.Elapsed.TotalSeconds;
        if (totalSeconds < 0.001) totalSeconds = 0.001;
        var speed = (totalBytes / 1024.0) / totalSeconds;

        Console.WriteLine($"Fetched {totalSizeStr} in {sw.Elapsed.Seconds}s ({speed:N0} kB/s)");

        if (allPackages.Count != 0)
        {
            var firstPkgInfo = allPackages.First();
            var pkg = firstPkgInfo.Package;
            var source = firstPkgInfo.Source; // Not strictly needed as DownloadPackageAsync is on Source

            var fileName = Path.GetFileName(pkg.Filename);
            var dest = Path.GetFullPath(fileName);

            Console.WriteLine($"\nDownloading 0th package of 0th source: {pkg.Package} ({pkg.Version})");
            Console.WriteLine($"Source: {source.ServerUrl} {source.Suite}");
            // Use Extras to avoid lint waning about unused collection
            Console.WriteLine($"Extras count: {pkg.Extras.Count}");
            Console.WriteLine($"Target: {dest}");

            await source.DownloadPackageAsync(pkg, dest, (downloaded, total) =>
            {
                if (total > 0)
                {
                    Console.Write($"\rDownloading... {downloaded * 100 / total}% ({FormatBytes(downloaded)}/{FormatBytes(total)})");
                }
            });
            Console.WriteLine("\nDownload and Verification Complete!");
        }
        else
        {
            Console.WriteLine("No packages found to download.");
        }
    }

    static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var kb = bytes / 1024.0;
        if (kb < 1000) return $"{kb:N1} kB".Replace(".0 kB", " kB");
        var mb = kb / 1024.0;
        if (mb < 1000) return $"{mb:N1} MB";
        var gb = mb / 1024.0;
        return $"{gb:N1} GB";
    }
}
