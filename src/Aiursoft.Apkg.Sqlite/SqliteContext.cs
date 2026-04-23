using System.Diagnostics.CodeAnalysis;
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Sqlite;

[ExcludeFromCodeCoverage]

public class SqliteContext(DbContextOptions<SqliteContext> options) : ApkgDbContext(options)
{
    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
