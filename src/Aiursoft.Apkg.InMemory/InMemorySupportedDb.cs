using Aiursoft.DbTools;
using Aiursoft.DbTools.InMemory;
using Aiursoft.Apkg.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.InMemory;

public class InMemorySupportedDb : SupportedDatabaseType<TemplateDbContext>
{
    public override string DbType => "InMemory";

    public override IServiceCollection RegisterFunction(IServiceCollection services, string connectionString)
    {
        return services.AddAiurInMemoryDb<InMemoryContext>();
    }

    public override TemplateDbContext ContextResolver(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<InMemoryContext>();
    }
}
