using System.Text.Json;

namespace Aiursoft.Apkg.Sdk.Models;

/// <summary>The durable receipt for a chunked upload's background processing.</summary>
public class ChunkedUploadStatus
{
    public required string Status { get; set; }
    public int? StatusCode { get; set; }
    public JsonElement? Result { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
