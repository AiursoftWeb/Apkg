using Aiursoft.Apkg.Entities;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Apkg.Services;

public class AptMetadataService : ITransientDependency
{
    public async Task WritePackageEntryAsync(StreamWriter writer, AptPackage pkg)
    {
        await writer.WriteLineAsync($"Package: {pkg.Package}");
        await writer.WriteLineAsync($"Architecture: {pkg.Architecture}");
        await writer.WriteLineAsync($"Version: {pkg.Version}");
        await writer.WriteLineAsync($"Priority: {pkg.Priority}");
        await writer.WriteLineAsync($"Section: {pkg.Section}");
        await writer.WriteLineAsync($"Origin: {pkg.Origin}");
        await writer.WriteLineAsync($"Maintainer: {pkg.Maintainer}");
        if (!string.IsNullOrWhiteSpace(pkg.OriginalMaintainer)) await writer.WriteLineAsync($"Original-Maintainer: {pkg.OriginalMaintainer}");
        await writer.WriteLineAsync($"Bugs: {pkg.Bugs}");
        await writer.WriteLineAsync($"Installed-Size: {pkg.InstalledSize}");
        if (!string.IsNullOrWhiteSpace(pkg.Depends)) await writer.WriteLineAsync($"Depends: {pkg.Depends}");
        if (!string.IsNullOrWhiteSpace(pkg.Recommends)) await writer.WriteLineAsync($"Recommends: {pkg.Recommends}");
        if (!string.IsNullOrWhiteSpace(pkg.Suggests)) await writer.WriteLineAsync($"Suggests: {pkg.Suggests}");
        if (!string.IsNullOrWhiteSpace(pkg.Conflicts)) await writer.WriteLineAsync($"Conflicts: {pkg.Conflicts}");
        if (!string.IsNullOrWhiteSpace(pkg.Breaks)) await writer.WriteLineAsync($"Breaks: {pkg.Breaks}");
        if (!string.IsNullOrWhiteSpace(pkg.Replaces)) await writer.WriteLineAsync($"Replaces: {pkg.Replaces}");
        if (!string.IsNullOrWhiteSpace(pkg.Provides)) await writer.WriteLineAsync($"Provides: {pkg.Provides}");
        if (!string.IsNullOrWhiteSpace(pkg.Source)) await writer.WriteLineAsync($"Source: {pkg.Source}");
        if (!string.IsNullOrWhiteSpace(pkg.Homepage)) await writer.WriteLineAsync($"Homepage: {pkg.Homepage}");
        await writer.WriteLineAsync($"Filename: {pkg.Filename}");
        await writer.WriteLineAsync($"Size: {pkg.Size}");
        await writer.WriteLineAsync($"MD5sum: {pkg.MD5sum}");
        await writer.WriteLineAsync($"SHA1: {pkg.SHA1}");
        await writer.WriteLineAsync($"SHA256: {pkg.SHA256}");
        if (!string.IsNullOrWhiteSpace(pkg.SHA512)) await writer.WriteLineAsync($"SHA512: {pkg.SHA512}");
        if (!string.IsNullOrWhiteSpace(pkg.MultiArch)) await writer.WriteLineAsync($"Multi-Arch: {pkg.MultiArch}");
        await writer.WriteLineAsync($"Description: {pkg.Description}");
        await writer.WriteLineAsync($"Description-md5: {pkg.DescriptionMd5}");
        foreach (var extra in pkg.Extras)
        {
            await writer.WriteLineAsync($"{extra.Key}: {extra.Value}");
        }
        await writer.WriteLineAsync();
    }
}
