using Aiursoft.DbTools;
using Aiursoft.DbTools.InMemory;
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.InMemory;

public class InMemorySupportedDb : SupportedDatabaseType<ApkgDbContext>
{
    public override string DbType => "InMemory";

    public override IServiceCollection RegisterFunction(IServiceCollection services, string connectionString)
    {
        // Use the connection string as the InMemory database name so
        // each host can have its own isolated database.
        var dbName = string.IsNullOrEmpty(connectionString)
            ? "ApkgInMemory"
            : connectionString;
        services.AddDbContext<InMemoryContext>(options =>
            options.UseInMemoryDatabase(dbName));
        return services;
    }

    public override ApkgDbContext ContextResolver(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<InMemoryContext>();
    }
}
