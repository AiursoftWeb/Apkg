using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

public enum PackageTestStatus { Passed, Failed, TimedOut, Cancelled, NotConfigured }

public record PackageTestResult(
    string Project, string Profile, string Name, PackageTestStatus Status,
    double DurationSeconds = 0, int? ExitCode = null,
    string StandardOutput = "", string StandardError = "");

/// <summary>
/// Executes explicitly selected source-test commands. Does not resolve build
/// targets, install dependencies, execute prebuild hooks, or parse test frameworks.
/// </summary>
public class PackageTestRunner(AosprojSerializer serializer)
{
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int KillProcessGroup(int pid, int signal);

    private const int OutputLimit = 64 * 1024;
    private static readonly HashSet<string> ExcludedDirectories =
        [".git", ".venv", "venv", "node_modules", "bin", "obj", "target"];

    public static IReadOnlyList<string> Validate(IEnumerable<TestCommandItem> commands)
    {
        var issues = new List<string>();
        var identities = new HashSet<(string, string)>();
        foreach (var command in commands)
        {
            if (!ValidIdentifier(command.Name))
                issues.Add("TestCommand Name must be a nonempty identifier (letters, digits, '.', '_' or '-').");
            if (!ValidIdentifier(command.Profile))
                issues.Add($"TestCommand '{command.Name}' must specify one valid Profile.");
            if (string.IsNullOrWhiteSpace(command.Run) || command.Run.Contains('\0'))
                issues.Add($"TestCommand '{command.Name}' must specify a nonempty Run without NUL characters.");
            if (command.TimeoutSeconds is < 1 or > 86400)
                issues.Add($"TestCommand '{command.Name}' TimeoutSeconds must be between 1 and 86400.");
            if (!identities.Add((command.Profile, command.Name)))
                issues.Add($"Duplicate TestCommand '{command.Name}' in profile '{command.Profile}'.");
        }
        return issues;
    }

    public static bool ValidIdentifier(string value) =>
        Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z");

    public static IReadOnlyList<string> Discover(string path, bool recursive)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path))
        {
            if (recursive || !path.EndsWith(".aosproj", StringComparison.Ordinal))
                throw new ArgumentException("A project file requires a .aosproj extension and cannot use --recursive.");
            return [path];
        }
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        var result = new List<string>();
        Visit(path);
        if (result.Count == 0)
            throw new FileNotFoundException($"No .aosproj projects found in '{path}'.");
        return result;

        void Visit(string directory)
        {
            var projects = Directory.GetFiles(directory, "*.aosproj").Order(StringComparer.Ordinal).ToArray();
            if (projects.Length > 1)
                throw new InvalidDataException($"Ambiguous project directory: '{directory}' contains multiple .aosproj files.");
            result.AddRange(projects);
            // A package owns its subtree; never discover vendored projects in it.
            if (!recursive || projects.Length > 0)
                return;
            foreach (var child in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(child)) &&
                    !File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                    Visit(child);
            }
        }
    }

    public async Task<IReadOnlyList<TestCommandItem>> ReadCommandsAsync(string projectFile, string profile)
    {
        if (!ValidIdentifier(profile))
            throw new ArgumentException("An explicit, valid test profile is required.", nameof(profile));
        var project = await serializer.DeserializeFromFileAsync(projectFile);
        var issues = Validate(project.TestCommands);
        if (issues.Count != 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        return project.TestCommands.Where(c => c.Profile == profile).ToArray();
    }

    public async Task<IReadOnlyList<PackageTestResult>> RunAsync(
        string projectFile, string profile,
        Action<string, bool>? output = null, CancellationToken cancellationToken = default)
    {
        projectFile = Path.GetFullPath(projectFile);
        var commands = await ReadCommandsAsync(projectFile, profile);
        if (commands.Count == 0)
            return [new(projectFile, profile, profile, PackageTestStatus.NotConfigured)];
        var results = new List<PackageTestResult>();
        foreach (var command in commands)
        {
            results.Add(await RunCommandAsync(projectFile, command, output, cancellationToken));
            if (cancellationToken.IsCancellationRequested)
                break;
        }
        return results;
    }

    private static async Task<PackageTestResult> RunCommandAsync(
        string projectFile, TestCommandItem command, Action<string, bool>? output,
        CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var clock = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("/usr/bin/setsid")
        {
            WorkingDirectory = Path.GetDirectoryName(projectFile)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add("/bin/sh");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(command.Run);
        process.StartInfo.Environment["APKG_TEST_PROFILE"] = command.Profile;
        var status = PackageTestStatus.Failed;
        int? exitCode = null;
        Task readers = Task.CompletedTask;
        var started = false;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            output?.Invoke($"[{command.Profile}/{command.Name}]\n", false);
            started = process.Start();
            readers = Task.WhenAll(
                ReadAsync(process.StandardOutput, stdout, false, linked.Token),
                ReadAsync(process.StandardError, stderr, true, linked.Token));
            await Task.WhenAll(process.WaitForExitAsync(linked.Token), readers).WaitAsync(linked.Token);
            exitCode = process.ExitCode;
            status = exitCode == 0 ? PackageTestStatus.Passed : PackageTestStatus.Failed;
        }
        catch (OperationCanceledException)
        {
            status = cancellationToken.IsCancellationRequested
                ? PackageTestStatus.Cancelled : PackageTestStatus.TimedOut;
        }
        catch (Exception error) when (error is Win32Exception or IOException)
        {
            AppendBounded(stderr, error.Message);
        }
        finally
        {
            if (started)
            {
                // A child can outlive its shell and still hold our pipes open.
                // Each entry owns a session, so clean up that process group too.
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { } // The process exited during the check.
                // Enumerate descendants before killing the shell: PTYs can own
                // another session and become untraceable once reparented.
                KillProcessGroup(-process.Id, 9);
                if (!readers.IsCompleted)
                {
                    process.StandardOutput.Dispose();
                    process.StandardError.Dispose();
                }
                try { await readers; }
                catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
            }
        }
        return new(projectFile, command.Profile, command.Name, status,
            clock.Elapsed.TotalSeconds, exitCode, stdout.ToString(), stderr.ToString());

        async Task ReadAsync(
            StreamReader reader, StringBuilder buffer, bool isError, CancellationToken readCancellationToken)
        {
            var chunk = new char[4096];
            int length;
            while ((length = await reader.ReadAsync(chunk.AsMemory(), readCancellationToken)) > 0)
            {
                var text = new string(chunk, 0, length);
                AppendBounded(buffer, text);
                output?.Invoke(text, isError);
            }
        }
    }

    private static void AppendBounded(StringBuilder buffer, string text)
    {
        buffer.Append(text);
        if (buffer.Length > OutputLimit)
            buffer.Remove(0, buffer.Length - OutputLimit);
    }

    public static XDocument CreateReport(IEnumerable<PackageTestResult> results)
    {
        return new XDocument(new XElement("testsuites", results.GroupBy(r => (r.Project, r.Profile))
            .Select(group => new XElement("testsuite",
                new XAttribute("name", XmlText($"{group.Key.Project}:{group.Key.Profile}")),
                new XAttribute("tests", group.Count()),
                new XAttribute("failures", group.Count(IsFailure)),
                new XAttribute("skipped", group.Count(r => r.Status == PackageTestStatus.NotConfigured)),
                new XAttribute("time", group.Sum(r => r.DurationSeconds).ToString("F3", CultureInfo.InvariantCulture)),
                group.Select(result => new XElement("testcase",
                    new XAttribute("classname", XmlText(result.Project)),
                    new XAttribute("name", XmlText($"{result.Profile}/{result.Name}")),
                    new XAttribute("time", result.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture)),
                    result.Status == PackageTestStatus.NotConfigured
                        ? new XElement("skipped", new XAttribute("message", "Profile not configured")) : null,
                    IsFailure(result) ? new XElement("failure",
                        new XAttribute("type", result.Status),
                        new XAttribute("message", $"Entry {result.Status}; exit code: {result.ExitCode?.ToString() ?? "none"}"),
                        XmlText(result.StandardError)) : null,
                    new XElement("system-out", XmlText(result.StandardOutput)),
                    new XElement("system-err", XmlText(result.StandardError))))))));
    }

    public static bool IsFailure(PackageTestResult result) =>
        result.Status is PackageTestStatus.Failed or PackageTestStatus.TimedOut or PackageTestStatus.Cancelled;

    private static string XmlText(string value) => string.Concat(value.EnumerateRunes()
        .Where(r => r.Value is 9 or 10 or 13 or >= 0x20 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
        .Select(r => r.ToString()));
}
