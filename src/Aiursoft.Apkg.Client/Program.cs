using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.CommandFramework;
using Aiursoft.CommandFramework.Models;

args = NormalizeArgs(args);

return await new NestedCommandApp()
    .WithGlobalOptions(CommonOptionsProvider.VerboseOption)
    .WithFeature(new NewHandler())
    .WithFeature(new PackHandler())
    .WithFeature(new PushHandler())
    .WithFeature(new InstallHandler())
    .WithFeature(new UnpackHandler())
    .WithFeature(new AddSourceHandler())
    .RunAsync(args);

static string[] NormalizeArgs(string[] args)
{
    if (args.Length < 2)
        return args;

    if (string.Equals(args[0], "add-source", StringComparison.OrdinalIgnoreCase)
        && !args[1].StartsWith("-", StringComparison.Ordinal))
    {
        var rewritten = new List<string>
        {
            args[0],
            "--url",
            args[1]
        };
        rewritten.AddRange(args.Skip(2));
        return rewritten.ToArray();
    }

    return args;
}
