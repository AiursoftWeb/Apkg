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
        services.AddSingleton<SystemInfoProvider>();
        services.AddSingleton<AosprojSerializer>();
        services.AddSingleton<ConditionEvaluator>();
        services.AddSingleton<DebBuilder>();
        services.AddSingleton<AosprojLinter>();
        services.AddSingleton<AosprojDependencyValidator>();
        services.AddHttpClient<AptPackageIndexClient>();
        return services;
    }

    public static IServiceCollection AddApkgPush(this IServiceCollection services)
    {
        services.AddHttpClient<ApkgPushService>(client =>
        {
            // Default 100s is too short for large .apkg files (e.g. anduinos-why-ai is ~17GB).
            // The server-side /complete endpoint must merge chunks, verify SHA256, extract
            // .deb entries, and process each one — which can take several minutes.
            client.Timeout = TimeSpan.FromMinutes(30);
        });
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
