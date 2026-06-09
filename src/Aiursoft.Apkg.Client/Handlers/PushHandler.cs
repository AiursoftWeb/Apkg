using System.CommandLine;
using System.Text.Json;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class PushHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "push";
    protected override string Description => "Push an .apkg package to an Apkg server.";

    private static readonly Option<string> FileOption =
        new(name: "--file", aliases: ["-f"])
        {
            Description = "Path to the .apkg file to push.",
            Required = true
        };

    private static readonly Option<string> SourceOption =
        new(name: "--source", aliases: ["-s"])
        {
            Description = "Package source URL (e.g. https://apkg.aiursoft.com).",
            Required = true
        };

    private static readonly Option<string> ApiKeyOption =
        new(name: "--api-key", aliases: ["-k"])
        {
            Description = "The API key for the server.",
            Required = true
        };

    private static readonly Option<bool> SkipDuplicateOption =
        new(name: "--skip-duplicate")
        {
            Description = "If a package already exists, skip it and continue.",
            DefaultValueFactory = _ => false
        };

    private static readonly Option<bool> AllowDowngradeOption =
        new(name: "--allow-downgrade")
        {
            Description = "Allow uploading a version that is older than the currently published version.",
            DefaultValueFactory = _ => false
        };

    private static readonly Option<long> ChunkSizeOption =
        new(name: "--chunk-size")
        {
            Description = "Maximum size of each upload chunk in bytes. Files larger than this will be uploaded in chunks to bypass CDN limits. Set to 0 to disable chunked upload.",
            DefaultValueFactory = _ => 90L * 1024 * 1024 // 90 MB
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        FileOption,
        SourceOption,
        ApiKeyOption,
        SkipDuplicateOption,
        AllowDowngradeOption,
        ChunkSizeOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var file = context.GetValue(FileOption)!;
        var source = context.GetValue(SourceOption)!;
        var apiKey = context.GetValue(ApiKeyOption)!;
        var skipDuplicate = context.GetValue(SkipDuplicateOption);
        var allowDowngrade = context.GetValue(AllowDowngradeOption);
        var chunkSize = context.GetValue(ChunkSizeOption);

        var filePath = Path.GetFullPath(file);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($".apkg file not found: {filePath}");

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var pushService = services.GetRequiredService<ApkgPushService>();
        var logger = services.GetRequiredService<ILogger<PushHandler>>();

        var fileSize = new FileInfo(filePath).Length;
        string responseBody;

        if (chunkSize > 0 && fileSize > chunkSize)
        {
            var chunkCount = (int)Math.Ceiling((double)fileSize / chunkSize);
            logger.LogInformation(
                "File {FileName} is {FileSize}MB — using chunked upload ({ChunkCount} chunks of up to {ChunkSize}MB)",
                Path.GetFileName(filePath),
                fileSize / (1024 * 1024),
                chunkCount,
                chunkSize / (1024 * 1024));

            var progress = new Progress<ChunkedUploadProgress>(p =>
            {
                logger.LogInformation("  {Message}", p.Message);
            });

            responseBody = await pushService.PushChunkedAsync(
                filePath, source, apiKey, skipDuplicate, allowDowngrade, chunkSize, progress);
        }
        else
        {
            logger.LogInformation("Pushing {FileName} to {Source}...", Path.GetFileName(filePath), source);
            responseBody = await pushService.PushAsync(filePath, source, apiKey, skipDuplicate, allowDowngrade);
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var hasErrors = false;

        if (TryGetPropertyIgnoreCase(root, "uploadId", out var uploadId) && uploadId.ValueKind == JsonValueKind.Number)
            logger.LogInformation("Upload record ID: {UploadId}", uploadId.GetInt32());

        if (TryGetPropertyIgnoreCase(root, "uploaded", out var uploaded) && uploaded.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in uploaded.EnumerateArray())
            {
                logger.LogInformation(
                    "  ✓ Uploaded to {Repository}: {Package} {Version} ({Arch})",
                    GetStringProperty(item, "repository"),
                    GetStringProperty(item, "package"),
                    GetStringProperty(item, "version"),
                    GetStringProperty(item, "arch"));
            }
        }

        if (TryGetPropertyIgnoreCase(root, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
        {
            foreach (var warning in warnings.EnumerateArray())
                logger.LogWarning("  ⚠ {Warning}", warning.GetString());
        }

        if (TryGetPropertyIgnoreCase(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errors.EnumerateArray())
            {
                hasErrors = true;
                logger.LogError("  ✗ {Error}", error.GetString());
            }
        }

        if (hasErrors)
            throw new InvalidOperationException("Push completed with errors.");

        logger.LogInformation("Push complete.");
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return TryGetPropertyIgnoreCase(element, propertyName, out var value) ? value.GetString() : null;
    }
}

