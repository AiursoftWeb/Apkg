using Aiursoft.CommandFramework.Abstracts;
using Aiursoft.Apkg.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.Client;

public class Startup : IStartUp
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddApkgLocalTools();
        services.AddApkgPush();
    }
}
