using System.Text.Json;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

public class ApkgSourceService(HttpClient httpClient)
{
    public async Task<SourceConfig> GetSourceConfigAsync(string url)
    {
        using var response = await httpClient.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to fetch source config from {url}: {response.StatusCode}\n{body}");

        var config = JsonSerializer.Deserialize<SourceConfig>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config == null)
            throw new InvalidOperationException("Failed to parse source config JSON.");

        return config;
    }
}
