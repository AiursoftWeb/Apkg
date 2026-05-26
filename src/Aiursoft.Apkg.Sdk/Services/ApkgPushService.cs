using System.Net;
using System.Net.Http.Headers;

namespace Aiursoft.Apkg.Sdk.Services;

public class ApkgPushService(HttpClient httpClient)
{
    /// <summary>
    /// Pushes an .apkg file to the server.
    /// Returns the JSON response body as a string.
    /// Throws HttpRequestException on network failure.
    /// Throws InvalidOperationException with server error message on HTTP error.
    /// </summary>
    public async Task<string> PushAsync(string apkgFilePath, string serverUrl, string apiKey, bool skipDuplicate)
    {
        serverUrl = serverUrl.TrimEnd('/');

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(apkgFilePath);
        using var fileContent = new StreamContent(fileStream)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
        };
        content.Add(fileContent, "apkg", Path.GetFileName(apkgFilePath));

        var url = $"{serverUrl}/api/packages/apkg-upload?skipDuplicate={skipDuplicate}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
            throw new InvalidOperationException($"Server returned {(int)response.StatusCode}: {body}");

        return body;
    }
}
