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

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        FileOption,
        SourceOption,
        ApiKeyOption,
        SkipDuplicateOption,
        AllowDowngradeOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var file = context.GetValue(FileOption)!;
        var source = context.GetValue(SourceOption)!;
        var apiKey = context.GetValue(ApiKeyOption)!;
        var skipDuplicate = context.GetValue(SkipDuplicateOption);
        var allowDowngrade = context.GetValue(AllowDowngradeOption);

        var filePath = Path.GetFullPath(file);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($".apkg file not found: {filePath}");

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var pushService = services.GetRequiredService<ApkgPushService>();
        var logger = services.GetRequiredService<ILogger<PushHandler>>();

        logger.LogInformation("Pushing {FileName} to {Source}...", Path.GetFileName(filePath), source);
        var responseBody = await pushService.PushAsync(filePath, source, apiKey, skipDuplicate, allowDowngrade);

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
