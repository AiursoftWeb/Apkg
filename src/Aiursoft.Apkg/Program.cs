using System.Diagnostics.CodeAnalysis;
using Aiursoft.ClickhouseLoggerProvider;
using Aiursoft.DbTools;
using Aiursoft.Apkg.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.Apkg;

[ExcludeFromCodeCoverage]
public abstract class Program
{
    public static async Task Main(string[] args)
    {
        var app = await AppAsync<Startup>(args);
        await app.Services.InitLoggingTableAsync();
        await app.UpdateDbAsync<ApkgDbContext>();
        await app.SeedAsync();
        await app.CopyAvatarFileAsync();
        await app.RunAsync();
    }
}
