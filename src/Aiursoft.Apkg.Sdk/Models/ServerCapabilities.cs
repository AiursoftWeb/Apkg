namespace Aiursoft.Apkg.Sdk.Models;

public sealed class ServerCapabilities
{
    public List<int> ApkgManifestVersions { get; set; } = [];
    public List<string> Features { get; set; } = [];
}
