using System.Diagnostics.CodeAnalysis;
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.MySql;

[ExcludeFromCodeCoverage]

public class MySqlContext(DbContextOptions<MySqlContext> options) : ApkgDbContext(options);
