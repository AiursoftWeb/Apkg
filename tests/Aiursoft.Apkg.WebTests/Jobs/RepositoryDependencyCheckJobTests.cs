using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Aiursoft.Apkg.Sqlite;

namespace Aiursoft.Apkg.WebTests.Jobs;

[TestClass]
public class RepositoryDependencyCheckJobTests
{
    private ServiceProvider _serviceProvider = null!;
    private ApkgDbContext _dbContext = null!;
    private RepositoryDependencyCheckJob _job = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        var dbName = $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();
        services.AddDbContext<ApkgDbContext, SqliteContext>(options => options.UseSqlite(dbName));
        
        services.AddSingleton<AptVersionComparisonService>();
        services.AddLogging();
        
        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ApkgDbContext>();
        
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var versionCompare = _serviceProvider.GetRequiredService<AptVersionComparisonService>();
        var logger = NullLogger<RepositoryDependencyCheckJob>.Instance;
        
        _job = new RepositoryDependencyCheckJob(logger, scopeFactory, versionCompare);
    }

    private AptPackage CreatePackage(AptBucket bucket, string name, string version, string arch, string? depends = null, string? provides = null)
    {
        return new AptPackage
        {
            Package = name,
            Version = version,
            Architecture = arch,
            Depends = depends,
            Provides = provides,
            Bucket = bucket,
            MD5sum = "1",
            SHA1 = "1",
            SHA256 = "1",
            SHA512 = "1",
            Size = "1",
            Component = "main",
            Filename = "a",
            OriginSuite = "test",
            OriginComponent = "main",
            Maintainer = "test",
            Description = "test",
            DescriptionMd5 = "1",
            Section = "test",
            Priority = "optional",
            Origin = "test",
            Bugs = "test"
        };
    }

    [TestMethod]
    public async Task RunAsync_VirtualPackages_ResolvesCorrectly()
    {
        await _dbContext.Database.EnsureCreatedAsync();

        // Arrange
        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        var repo = new AptRepository 
        { 
            Name = "test-repo", 
            Suite = "test", 
            Components = "main", 
            Architecture = "amd64",
            PrimaryBucket = bucket
        };

        _dbContext.AptBuckets.Add(bucket);
        _dbContext.AptRepositories.Add(repo);

        // Package that needs a virtual dependency
        var accountsservice = CreatePackage(bucket, "accountsservice", "1.0", "amd64", depends: "dbus-system-bus");

        // Package that provides the virtual dependency (unversioned)
        var dbus = CreatePackage(bucket, "dbus", "1.0", "amd64", provides: "dbus-system-bus, default-dbus-system-bus");

        // Package that needs a versioned virtual dependency
        var adduser = CreatePackage(bucket, "adduser", "1.0", "all", depends: "passwd (>= 1:4.17.2-5)");

        // Package that provides the versioned dependency
        var mypasswd = CreatePackage(bucket, "mypattern", "1.0", "amd64", provides: "passwd (= 1:4.17.4-2ubuntu2)");

        _dbContext.AptPackages.AddRange(accountsservice, dbus, adduser, mypasswd);
        await _dbContext.SaveChangesAsync();

        // Act
        var reportId = await _job.RunAsync(repo.Id);

        // Assert
        var report = await _dbContext.DependencyCheckReports.FindAsync(reportId);
        Assert.IsNotNull(report);
        Assert.AreEqual(4, report.TotalPackages);
        
        // Output details json if there are problematic packages to help debug
        if (report.ProblematicPackages > 0)
        {
            Console.WriteLine(report.DetailsJson);
        }
        
        Assert.AreEqual(0, report.ProblematicPackages, "Should be 0 problematic packages");
    }
    [TestMethod]
    public async Task RunAsync_ArchitectureShadowing_ResolvesCorrectly()
    {
        await _dbContext.Database.EnsureCreatedAsync();

        // Arrange
        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        var repo = new AptRepository 
        { 
            Name = "test-repo", 
            Suite = "test", 
            Components = "main", 
            Architecture = "amd64",
            PrimaryBucket = bucket
        };

        _dbContext.AptBuckets.Add(bucket);
        _dbContext.AptRepositories.Add(repo);

        // gnupg (all) depends on gpg (>= 2.4.8)
        var gnupg = CreatePackage(bucket, "gnupg", "2.4.8", "all", depends: "gpg (>= 2.4.8)");

        // gpg-from-sq (all) provides gpg (= 2.2.46), which DOES NOT satisfy the constraint
        var gpgFromSq = CreatePackage(bucket, "gpg-from-sq", "0.13", "all", provides: "gpg (= 2.2.46)");

        // gpg (amd64) provides itself (implicit) with version 2.4.8, which DOES satisfy the constraint
        var gpg = CreatePackage(bucket, "gpg", "2.4.8", "amd64");

        _dbContext.AptPackages.AddRange(gnupg, gpgFromSq, gpg);
        await _dbContext.SaveChangesAsync();

        // Act
        var reportId = await _job.RunAsync(repo.Id);

        // Assert
        var report = await _dbContext.DependencyCheckReports.FindAsync(reportId);
        Assert.IsNotNull(report);
        Assert.AreEqual(3, report.TotalPackages);
        
        if (report.ProblematicPackages > 0)
        {
            Console.WriteLine(report.DetailsJson);
        }
        
        Assert.AreEqual(0, report.ProblematicPackages, "Should be 0 problematic packages. The real gpg package should not be shadowed by gpg-from-sq.");
    }
}
