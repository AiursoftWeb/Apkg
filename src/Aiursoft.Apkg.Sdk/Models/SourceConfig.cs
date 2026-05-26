using System.Text.Json.Serialization;

namespace Aiursoft.Apkg.Sdk.Models;

public class SourceConfig
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("distro")]
    public string Distro { get; set; } = string.Empty;

    [JsonPropertyName("suite")]
    public string Suite { get; set; } = string.Empty;

    [JsonPropertyName("components")]
    public string Components { get; set; } = "main";

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = "amd64";

    [JsonPropertyName("aptBaseUrl")]
    public string AptBaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("enableGpgSign")]
    public bool EnableGpgSign { get; set; }

    [JsonPropertyName("keyUrl")]
    public string? KeyUrl { get; set; }

    [JsonPropertyName("keyFileName")]
    public string? KeyFileName { get; set; }

    [JsonPropertyName("sourcesFileName")]
    public string SourcesFileName { get; set; } = string.Empty;
}
