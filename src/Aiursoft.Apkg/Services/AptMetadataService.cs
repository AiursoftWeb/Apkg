using Aiursoft.Apkg.Entities;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Apkg.Services;

public class AptMetadataService : ITransientDependency
{
    private async Task WriteField(StreamWriter writer, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var formatted = value.Replace("\n", "\n ");
        await writer.WriteLineAsync($"{key}: {formatted}");
    }

    public async Task WritePackageEntryAsync(StreamWriter writer, AptPackage pkg)
    {
        await WriteField(writer, "Package", pkg.Package);
        await WriteField(writer, "Architecture", pkg.Architecture);
        await WriteField(writer, "Version", pkg.Version);
        await WriteField(writer, "Priority", pkg.Priority);
        await WriteField(writer, "Section", pkg.Section);
        await WriteField(writer, "Origin", pkg.Origin);
        await WriteField(writer, "Maintainer", pkg.Maintainer);
        await WriteField(writer, "Original-Maintainer", pkg.OriginalMaintainer);
        await WriteField(writer, "Bugs", pkg.Bugs);
        await WriteField(writer, "Installed-Size", pkg.InstalledSize);
        await WriteField(writer, "Depends", pkg.Depends);
        await WriteField(writer, "Recommends", pkg.Recommends);
        await WriteField(writer, "Suggests", pkg.Suggests);
        await WriteField(writer, "Conflicts", pkg.Conflicts);
        await WriteField(writer, "Breaks", pkg.Breaks);
        await WriteField(writer, "Replaces", pkg.Replaces);
        await WriteField(writer, "Provides", pkg.Provides);
        await WriteField(writer, "Source", pkg.Source);
        await WriteField(writer, "Homepage", pkg.Homepage);
        await WriteField(writer, "Filename", pkg.Filename);
        await WriteField(writer, "Size", pkg.Size);
        await WriteField(writer, "MD5sum", pkg.MD5sum);
        await WriteField(writer, "SHA1", pkg.SHA1);
        await WriteField(writer, "SHA256", pkg.SHA256);
        await WriteField(writer, "SHA512", pkg.SHA512);
        await WriteField(writer, "Multi-Arch", pkg.MultiArch);
        await WriteField(writer, "Description", pkg.Description);
        await WriteField(writer, "Description-md5", pkg.DescriptionMd5);
        foreach (var extra in pkg.Extras)
        {
            await WriteField(writer, extra.Key, extra.Value);
        }
        await writer.WriteLineAsync();
    }
}
