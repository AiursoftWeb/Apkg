using System.CommandLine;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class TestHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "test";
    protected override string Description => "Run an explicit source-test profile without building packages.";

    private static readonly Option<string> PathOption = new("--path", ["-p"])
    {
        Description = "Project file or directory to inspect.",
        DefaultValueFactory = _ => "."
    };
    private static readonly Option<string> ProfileOption = new("--profile")
    {
        Description = "Exact profile to select; no other profiles are executed.",
        Required = true
    };
    private static readonly Option<bool> RecursiveOption = new("--recursive")
    {
        Description = "Discover packages below --path, excluding build output and symlink directories."
    };
    private static readonly Option<bool> ListOption = new("--list")
    {
        Description = "List matching entries without executing commands."
    };
    private static readonly Option<string?> ReportOption = new("--report")
    {
        Description = "Write a JUnit XML report (one test case per entry, not per framework test)."
    };

    protected override IEnumerable<Option> GetCommandOptions() =>
        [PathOption, ProfileOption, RecursiveOption, ListOption, ReportOption];

    protected override async Task Execute(ParseResult context)
    {
        var profile = context.GetValue(ProfileOption)!;
        var report = context.GetValue(ReportOption);
        var listOnly = context.GetValue(ListOption);
        if (!PackageTestRunner.ValidIdentifier(profile))
            throw new ArgumentException("Invalid --profile identifier.");
        if (listOnly && report != null)
            throw new ArgumentException("--report cannot be used with --list.");
        var projects = PackageTestRunner.Discover(context.GetValue(PathOption)!, context.GetValue(RecursiveOption));
        using var host = ServiceBuilder.CreateCommandHostBuilder<Startup>(
            context.GetValue(CommonOptionsProvider.VerboseOption)).Build();
        var runner = host.Services.GetRequiredService<PackageTestRunner>();
        var logger = host.Services.GetRequiredService<ILogger<TestHandler>>();
        using var cancellation = new ConsoleCancellation();
        var results = new List<PackageTestResult>();
        try
        {
            foreach (var project in projects)
            {
                if (cancellation.IsCancellationRequested)
                    break;
                logger.LogInformation("Tests: {Project}, profile: {Profile}", project, profile);
                try
                {
                    if (listOnly)
                    {
                        var commands = await runner.ReadCommandsAsync(project, profile);
                        if (commands.Count == 0)
                            logger.LogInformation("Profile not configured.");
                        foreach (var command in commands)
                            logger.LogInformation("{Name}: {Run} (timeout {Timeout}s)",
                                command.Name, command.Run, command.TimeoutSeconds);
                        continue;
                    }
                    var projectResults = await runner.RunAsync(project, profile,
                        (text, error) => (error ? Console.Error : Console.Out).Write(text), cancellation.Token);
                    results.AddRange(projectResults);
                    foreach (var result in projectResults)
                        logger.LogInformation("{Name}: {Status}", result.Name, result.Status);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    logger.LogError("{Project}: {Error}", project, error.Message);
                    results.Add(new(project, profile, "configuration", PackageTestStatus.Failed,
                        StandardError: error.Message));
                }
            }
            if (report != null)
            {
                var fullPath = Path.GetFullPath(report);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                PackageTestRunner.CreateReport(results).Save(fullPath);
            }
            if (!listOnly)
                logger.LogInformation(
                    "Summary: {Projects} projects; {Passed} passed entries; {Failed} failed entries; {Unconfigured} projects not configured for {Profile}.",
                    projects.Count, results.Count(r => r.Status == PackageTestStatus.Passed),
                    results.Count(PackageTestRunner.IsFailure),
                    results.Count(r => r.Status == PackageTestStatus.NotConfigured), profile);
            if (cancellation.IsCancellationRequested)
                throw new OperationCanceledException("Test run cancelled.");
            if (results.Any(PackageTestRunner.IsFailure))
                throw new InvalidOperationException("Package tests failed. See entry results above.");
        }
        finally { cancellation.Unsubscribe(); }
    }

    private sealed class ConsoleCancellation : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private bool _subscribed = true;

        public ConsoleCancellation() => Console.CancelKeyPress += Cancel;

        public CancellationToken Token => _source.Token;
        public bool IsCancellationRequested => _source.IsCancellationRequested;

        private void Cancel(object? _, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _source.Cancel();
        }

        public void Unsubscribe()
        {
            if (!_subscribed)
                return;
            Console.CancelKeyPress -= Cancel;
            _subscribed = false;
        }

        public void Dispose()
        {
            Unsubscribe();
            _source.Dispose();
        }
    }
}
