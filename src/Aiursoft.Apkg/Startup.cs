using Aiursoft.CSTools.Tools;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;
using Aiursoft.DbTools.Switchable;
using Aiursoft.Scanner;
using Aiursoft.Apkg.Configuration;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.Apkg.InMemory;
using Aiursoft.Apkg.MySql;
using Aiursoft.Apkg.Services.Authentication;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Sqlite;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc.Razor;
using Aiursoft.ClickhouseLoggerProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg;

[ExcludeFromCodeCoverage]
public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

        // Relational database
        var (connectionString, dbType, allowCache) = configuration.GetDbSettings();
        services.AddSwitchableRelationalDatabase(
            dbType: EntryExtends.IsInUnitTests() ? "InMemory" : dbType,
            connectionString: connectionString,
            supportedDbs:
            [
                new MySqlSupportedDb(allowCache: allowCache, splitQuery: false),
                new SqliteSupportedDb(allowCache: allowCache, splitQuery: true),
                new InMemorySupportedDb()
            ]);

        services.AddLogging(builder =>
        {
            builder.AddClickhouse(options => configuration.GetSection("Logging:Clickhouse").Bind(options));
        });

        // Authentication and Authorization
        services.AddTemplateAuth(configuration);

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddTransient<IGpgSigningService, GpgSigningService>();
        services.AddSingleton<NavigationState<Startup>>();

        // Explicitly register dependency check services
        services.AddTransient<AptVersionComparisonService>();
        services.AddTransient<RepositoryDependencyCheckJob>();

        // Background job infrastructure
        services.AddTaskQueueEngine();
        services.AddScheduledTaskEngine();

        // Background jobs
        var orphanAvatarCleanupJob = services.RegisterBackgroundJob<OrphanAvatarCleanupJob>();
        var mirrorSyncJob = services.RegisterBackgroundJob<MirrorSyncJob>();
        var repositorySyncJob = services.RegisterBackgroundJob<RepositorySyncJob>();
        var repositorySignJob = services.RegisterBackgroundJob<RepositorySignJob>();
        var garbageCollectionJob = services.RegisterBackgroundJob<GarbageCollectionJob>();

        // Scheduled tasks (attach a schedule to any registered background job)
        services.RegisterScheduledTask(
            registration: orphanAvatarCleanupJob,
            period: TimeSpan.FromHours(6),
            startDelay: TimeSpan.FromMinutes(5));

        // Mirror Job runs every 20 minutes, delay 10 minutes.
        services.RegisterScheduledTask(
            registration: mirrorSyncJob,
            period: TimeSpan.FromMinutes(20),
            startDelay: TimeSpan.FromMinutes(10));

        // Repository Sync Job runs every 20 minutes, delay 20 minutes.
        services.RegisterScheduledTask(
            registration: repositorySyncJob,
            period: TimeSpan.FromMinutes(20),
            startDelay: TimeSpan.FromMinutes(20));

        // Repository Sign Job runs every 5 minutes (signs and promotes any pending buckets after sync).
        services.RegisterScheduledTask(
            registration: repositorySignJob,
            period: TimeSpan.FromMinutes(5),
            startDelay: TimeSpan.FromMinutes(25));

        // Garbage Collection Job runs every 70 minutes, delay 15 minutes.
        services.RegisterScheduledTask(
            registration: garbageCollectionJob,
            period: TimeSpan.FromMinutes(70),
            startDelay: TimeSpan.FromMinutes(15));

        // So an idea run steps are:
        // 1. At 10:00, Mirror Sync Job runs
        // 2. At 10:15, Garbage Collection Job runs
        // 3. At 10:20, Repository Sync Job runs
        // 4. At 10:30, Mirror Sync Job runs again
        // 5. At 10:40, Repository Sync Job runs again
        // 6. At 10:50, Mirror Sync Job runs again
        // 7. At 11:00, Repository Sync Job runs again
        // 8. At 11:10, Mirror Sync Job runs again
        // 9. At 11:20, Repository Sync Job runs again
        // 10. At 11:25, Garbage Collection Job runs again
        // 11. At 11:30, Mirror Sync Job runs again
        // 12. At 11:40, Repository Sync Job runs again

        // Controllers and localization
        services.AddControllersWithViews()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
            })
            .AddApplicationPart(typeof(Startup).Assembly)
            .AddApplicationPart(typeof(UiStackLayoutViewModel).Assembly)
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization();
    }

    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler("/Error/Code500");
        app.UseStatusCodePagesWithReExecute("/Error/Code{0}");
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapDefaultControllerRoute();
    }
}
