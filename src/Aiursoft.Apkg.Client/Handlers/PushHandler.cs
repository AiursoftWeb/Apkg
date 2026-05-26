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

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        FileOption,
        SourceOption,
        ApiKeyOption,
        SkipDuplicateOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var file = context.GetValue(FileOption)!;
        var source = context.GetValue(SourceOption)!;
        var apiKey = context.GetValue(ApiKeyOption)!;
        var skipDuplicate = context.GetValue(SkipDuplicateOption);

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
        var responseBody = await pushService.PushAsync(filePath, source, apiKey, skipDuplicate);

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var hasErrors = false;

        if (root.TryGetProperty("uploaded", out var uploaded) && uploaded.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in uploaded.EnumerateArray())
            {
                logger.LogInformation(
                    "  ✓ Uploaded to {Repository}: {Package} {Version} ({Arch})",
                    item.GetProperty("repository").GetString(),
                    item.GetProperty("package").GetString(),
                    item.GetProperty("version").GetString(),
                    item.GetProperty("arch").GetString());
            }
        }

        if (root.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
        {
            foreach (var warning in warnings.EnumerateArray())
                logger.LogWarning("  ⚠ {Warning}", warning.GetString());
        }

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
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
}
