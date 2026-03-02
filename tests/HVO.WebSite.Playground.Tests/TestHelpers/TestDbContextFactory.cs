using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HVO.DataModels.Data;

namespace HVO.WebSite.Playground.Tests.TestHelpers;

/// <summary>
/// Factory for creating test database contexts with in-memory databases
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a new HvoDbContext with an in-memory database for testing
    /// Each call creates a separate database instance to ensure test isolation
    /// </summary>
    /// <param name="testName">Optional test name to create unique database instances</param>
    /// <returns>A configured HvoDbContext for testing</returns>
    public static HvoDbContext CreateInMemoryContext(string? testName = null)
    {
        var databaseName = testName ?? Guid.NewGuid().ToString();
        
        var options = new DbContextOptionsBuilder<HvoDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
            
        return new HvoDbContext(options);
    }

    /// <summary>
    /// Creates a service collection configured for testing with in-memory database
    /// </summary>
    /// <param name="testName">Optional test name to create unique database instances</param>
    /// <returns>A configured ServiceCollection for testing</returns>
    public static ServiceCollection CreateTestServiceCollection(string? testName = null)
    {
        var services = new ServiceCollection();
        var databaseName = testName ?? Guid.NewGuid().ToString();
        
        services.AddDbContext<HvoDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
            
        return services;
    }
}
