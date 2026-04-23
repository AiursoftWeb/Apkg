using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.InMemory;

public class InMemoryContext(DbContextOptions<InMemoryContext> options) : ApkgDbContext(options)
{
    public override Task MigrateAsync(CancellationToken cancellationToken)
    {
        return Database.EnsureCreatedAsync(cancellationToken);
    }

    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
