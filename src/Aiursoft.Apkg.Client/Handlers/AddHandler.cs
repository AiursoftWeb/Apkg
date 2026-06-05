using System.CommandLine;
using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

/// <summary>
/// <c>apkg add ./myfile.so --target /usr/lib/myfile.so</c>
/// Adds an IncludeFile (or IncludeFolder) entry to the .aosproj in the current directory.
/// </summary>
public class AddHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "add";
    protected override string Description => "Add a file or folder to the .aosproj package project.";

    private static readonly Argument<string> SourceArgument =
        new("source")
        {
            Description = "Local path of the file or directory to include (e.g. ./libfoo.so or ./sof/)."
        };

    private static readonly Option<string> TargetOption =
        new(name: "--target", aliases: ["-t"])
        {
            Description = "Absolute installation path inside the package (e.g. /usr/lib/libfoo.so).",
            Required = true
        };

    private static readonly Option<bool> ConfigOption =
        new(name: "--config")
        {
            Description = "Mark as a config file (ConfFile). dpkg will preserve user edits on upgrade.",
            DefaultValueFactory = _ => false
        };

    private static readonly Option<string> ConditionOption =
        new(name: "--condition")
        {
            Description = "MSBuild-style condition to scope this file to a specific suite or arch, e.g. \"'$(Suite)' == 'jammy'\".",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "Directory containing the .aosproj file (defaults to current directory).",
            DefaultValueFactory = _ => "."
        };

    private static readonly Option<string?> ModeOption =
        new(name: "--mode")
        {
            Description = "Unix permission mode in octal (e.g. 755, 644, 600). Sets Mode attribute on the item.",
            DefaultValueFactory = _ => null
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        TargetOption,
        ConfigOption,
        ConditionOption,
        PathOption,
        ModeOption,
    ];

    public override Command BuildAsCommand()
    {
        var command = base.BuildAsCommand();
        command.Arguments.Add(SourceArgument);
        foreach (var option in GetCommandOptions())
            command.Options.Add(option);
        command.SetAction(Execute);
        return command;
    }

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var source = context.GetValue(SourceArgument)!;
        var target = context.GetValue(TargetOption)!;
        var isConfig = context.GetValue(ConfigOption);
        var condition = context.GetValue(ConditionOption)!;
        var pathArg = context.GetValue(PathOption)!;
        var modeStr = context.GetValue(ModeOption);

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var logger = services.GetRequiredService<ILogger<AddHandler>>();

        var projectDir = Path.GetFullPath(pathArg);
        var projectFile = AosprojSerializer.FindProjectFile(projectDir);

        // Detect whether source is a directory
        var resolvedSource = Path.GetFullPath(Path.Combine(projectDir, source));
        var isDirectory = Directory.Exists(resolvedSource);

        // Determine element name
        string elementName;
        if (isConfig)
            elementName = "ConfFile";
        else if (isDirectory)
            elementName = "IncludeFolder";
        else
            elementName = "IncludeFile";

        // Build new item element
        var newItem = new XElement(elementName,
            new XAttribute("Include", source),
            new XAttribute("Target", target));
        if (!string.IsNullOrWhiteSpace(condition))
            newItem.Add(new XAttribute("Condition", condition));
        if (!string.IsNullOrWhiteSpace(modeStr))
        {
            if (modeStr.Length != 3 || modeStr.Any(c => c < '0' || c > '7'))
            {
                logger.LogError("Invalid --mode value '{Mode}'. Must be exactly 3 octal digits (e.g. 755, 644).", modeStr);
                return;
            }
            newItem.Add(new XAttribute("Mode", modeStr));
        }

        // Load and modify the XML directly to preserve formatting and comments
        var doc = XDocument.Load(projectFile);
        var root = doc.Root!;

        // Find last ItemGroup that contains IncludeFile/IncludeFolder/IncludeScript/ConfFile,
        // or create a new one.
        var fileItemGroup = root.Elements("ItemGroup")
            .LastOrDefault(ig => ig.Elements().Any(e =>
                e.Name.LocalName is "IncludeFile" or "IncludeFolder" or "IncludeScript" or "ConfFile"));

        if (fileItemGroup != null)
        {
            fileItemGroup.Add(newItem);
        }
        else
        {
            root.Add(new XElement("ItemGroup", newItem));
        }

        doc.Save(projectFile);

        logger.LogInformation("Added <{Element} Include=\"{Source}\" Target=\"{Target}\" /> to {File}",
            elementName, source, target, Path.GetFileName(projectFile));

        if (!File.Exists(resolvedSource) && !Directory.Exists(resolvedSource))
            logger.LogWarning("  ⚠ Source path does not exist yet: {Source}", resolvedSource);
    }
}
