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
    public async Task<string> PushAsync(string apkgFilePath, string serverUrl, string apiKey, bool skipDuplicate, bool allowDowngrade = false)
    {
        serverUrl = serverUrl.TrimEnd('/');

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(apkgFilePath);
        using var fileContent = CreateApkgFileContent(fileStream);
        content.Add(fileContent, "apkg", Path.GetFileName(apkgFilePath));

        var url = $"{serverUrl}/api/packages/apkg-upload?skipDuplicate={skipDuplicate}&allowDowngrade={allowDowngrade}";
        using var request = CreateRequest(url, content, apiKey);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode
            && response.StatusCode != HttpStatusCode.Conflict
            && response.StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException($"Server returned {(int)response.StatusCode}: {body}");

        return body;
    }

    private static StreamContent CreateApkgFileContent(Stream fileStream)
    {
        var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static HttpRequestMessage CreateRequest(string url, HttpContent content, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }
}
