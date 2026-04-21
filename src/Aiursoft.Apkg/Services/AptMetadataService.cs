using System.Security.Cryptography;
using System.Text;
using Aiursoft.Apkg.Entities;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Apkg.Services;

public class AptMetadataService : ITransientDependency
{
    public string GeneratePackagesFile(IEnumerable<AptPackage> packages)
    {
        var sb = new StringBuilder();
        foreach (var pkg in packages)
        {
            sb.AppendLine($"Package: {pkg.Package}");
            sb.AppendLine($"Architecture: {pkg.Architecture}");
            sb.AppendLine($"Version: {pkg.Version}");
            sb.AppendLine($"Priority: {pkg.Priority}");
            sb.AppendLine($"Section: {pkg.Section}");
            sb.AppendLine($"Origin: {pkg.Origin}");
            sb.AppendLine($"Maintainer: {pkg.Maintainer}");
            if (!string.IsNullOrWhiteSpace(pkg.OriginalMaintainer)) sb.AppendLine($"Original-Maintainer: {pkg.OriginalMaintainer}");
            sb.AppendLine($"Bugs: {pkg.Bugs}");
            sb.AppendLine($"Installed-Size: {pkg.InstalledSize}");
            if (!string.IsNullOrWhiteSpace(pkg.Depends)) sb.AppendLine($"Depends: {pkg.Depends}");
            if (!string.IsNullOrWhiteSpace(pkg.Recommends)) sb.AppendLine($"Recommends: {pkg.Recommends}");
            if (!string.IsNullOrWhiteSpace(pkg.Suggests)) sb.AppendLine($"Suggests: {pkg.Suggests}");
            if (!string.IsNullOrWhiteSpace(pkg.Conflicts)) sb.AppendLine($"Conflicts: {pkg.Conflicts}");
            if (!string.IsNullOrWhiteSpace(pkg.Breaks)) sb.AppendLine($"Breaks: {pkg.Breaks}");
            if (!string.IsNullOrWhiteSpace(pkg.Replaces)) sb.AppendLine($"Replaces: {pkg.Replaces}");
            if (!string.IsNullOrWhiteSpace(pkg.Provides)) sb.AppendLine($"Provides: {pkg.Provides}");
            if (!string.IsNullOrWhiteSpace(pkg.Source)) sb.AppendLine($"Source: {pkg.Source}");
            if (!string.IsNullOrWhiteSpace(pkg.Homepage)) sb.AppendLine($"Homepage: {pkg.Homepage}");
            sb.AppendLine($"Filename: {pkg.Filename}");
            sb.AppendLine($"Size: {pkg.Size}");
            sb.AppendLine($"MD5sum: {pkg.MD5sum}");
            sb.AppendLine($"SHA1: {pkg.SHA1}");
            sb.AppendLine($"SHA256: {pkg.SHA256}");
            if (!string.IsNullOrWhiteSpace(pkg.SHA512)) sb.AppendLine($"SHA512: {pkg.SHA512}");
            if (!string.IsNullOrWhiteSpace(pkg.MultiArch)) sb.AppendLine($"Multi-Arch: {pkg.MultiArch}");
            sb.AppendLine($"Description: {pkg.Description}");
            sb.AppendLine($"Description-md5: {pkg.DescriptionMd5}");
            foreach (var extra in pkg.Extras)
            {
                sb.AppendLine($"{extra.Key}: {extra.Value}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string GenerateReleaseFile(string suite, string component, string arch, string packagesContent)
    {
        var packagesBytes = Encoding.UTF8.GetBytes(packagesContent);
        var size = packagesBytes.Length;
        var md5 = BitConverter.ToString(MD5.HashData(packagesBytes)).Replace("-", "").ToLower();
        var sha1 = BitConverter.ToString(SHA1.HashData(packagesBytes)).Replace("-", "").ToLower();
        var sha256 = BitConverter.ToString(SHA256.HashData(packagesBytes)).Replace("-", "").ToLower();

        var sb = new StringBuilder();
        sb.AppendLine($"Archive: {suite}");
        sb.AppendLine($"Component: {component}");
        sb.AppendLine($"Origin: Aiursoft Apkg");
        sb.AppendLine($"Label: Aiursoft Apkg");
        sb.AppendLine($"Architecture: {arch}");
        sb.AppendLine($"Date: {DateTime.UtcNow:R}");
        sb.AppendLine("MD5Sum:");
        sb.AppendLine($" {md5} {size} {component}/binary-{arch}/Packages");
        sb.AppendLine("SHA1:");
        sb.AppendLine($" {sha1} {size} {component}/binary-{arch}/Packages");
        sb.AppendLine("SHA256:");
        sb.AppendLine($" {sha256} {size} {component}/binary-{arch}/Packages");
        return sb.ToString();
    }
}
