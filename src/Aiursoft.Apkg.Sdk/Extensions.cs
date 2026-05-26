using Aiursoft.AiurProtocol;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.Sdk;

public static class Extensions
{
    public static IServiceCollection AddApkgServer(this IServiceCollection services, string endPointUrl)
    {
        services.AddAiurProtocolClient();
        services.Configure<ServerConfig>(options => options.Instance = endPointUrl);
        services.AddScoped<ServerAccess>();
        return services;
    }

    public static IServiceCollection AddApkgLocalTools(this IServiceCollection services)
    {
        services.AddSingleton<ManifestSerializer>();
        services.AddSingleton<DebPackageValidator>();
        services.AddSingleton<SystemInfoProvider>();
        return services;
    }

    public static IServiceCollection AddApkgPush(this IServiceCollection services)
    {
        services.AddHttpClient<ApkgPushService>();
        return services;
    }

    public static IServiceCollection AddApkgSource(this IServiceCollection services)
    {
        services.AddHttpClient<ApkgSourceService>();
        return services;
    }

    public static Version GetSdkVersion()
    {
        return typeof(Extensions).Assembly.GetName().Version!;
    }
}
