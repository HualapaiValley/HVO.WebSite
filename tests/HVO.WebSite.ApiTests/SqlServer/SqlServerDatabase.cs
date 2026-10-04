using HVO.DataModels.Data;
using HVO.DataModels.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.WebSite.ApiTests.SqlServer;

/// <summary>A database owned by one invocation of the disposable SQL runner.</summary>
internal sealed class SqlServerDatabase : IAsyncDisposable
{
    private readonly string masterConnectionString;
    private readonly string databaseName;
    private readonly ServiceProvider services;

    private SqlServerDatabase(string server, string password, string runId)
    {
        databaseName = $"hvo_sql_test_{runId}_{Guid.NewGuid():N}";
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = "master", UserID = "sa", Password = password,
            Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 15
        };
        masterConnectionString = connection.ConnectionString;
        connection.InitialCatalog = databaseName;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:HualapaiValleyObservatory"] = connection.ConnectionString
        }).Build();
        var registration = new ServiceCollection();
        registration.AddHvoDataServices(configuration);
        services = registration.BuildServiceProvider();
    }

    public static async Task<SqlServerDatabase> CreateAsync(string? migration = null)
    {
        var server = Environment.GetEnvironmentVariable("HVO_SQL_TEST_SERVER");
        var password = Environment.GetEnvironmentVariable("HVO_SQL_TEST_PASSWORD");
        var runId = Environment.GetEnvironmentVariable("HVO_SQL_TEST_RUN_ID");
        ValidateConfiguration(server, password, runId);
        var database = new SqlServerDatabase(server!, password!, runId!);
        try
        {
            await using var connection = new SqlConnection(database.masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CAST(value AS nvarchar(32)) FROM sys.extended_properties WHERE class=0 AND name='hvo.sql-test-run'";
            if (!string.Equals(await command.ExecuteScalarAsync() as string, runId, StringComparison.Ordinal))
                throw new InvalidOperationException("SQL Server endpoint is not owned by this disposable fixture run.");
            command.CommandText = $"CREATE DATABASE [{database.databaseName}]";
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            await database.services.DisposeAsync();
            throw;
        }

        try
        {
            await using var db = database.CreateContext();
            if (migration is null)
                await db.Database.MigrateAsync();
            else
                await db.GetService<IMigrator>().MigrateAsync(migration);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    internal static void ValidateConfiguration(string? server, string? password, string? runId)
    {
        // No application connection string, arbitrary hostname, or production database is accepted.
        var parts = server?.Split(',');
        if (parts is not { Length: 2 } || parts[0] != "127.0.0.1"
            || !int.TryParse(parts[1], out var port) || port is < 1024 or > 65535
            || string.IsNullOrWhiteSpace(password)
            || runId is not { Length: 32 } || runId.Any(character => !char.IsAsciiHexDigit(character)))
            throw new InvalidOperationException("SQL Server fixture is unavailable or unsafe. Use tools/run-sql-server-integration-tests.sh; only its isolated loopback endpoint and run identity are accepted.");
    }

    public HvoV9DbContext CreateContext(params IInterceptor[] interceptors)
    {
        // Use the actual production registration: MARS, retry strategy, timeout and v9 history.
        var options = services.GetRequiredService<DbContextOptions<HvoV9DbContext>>();
        return new HvoV9DbContext(new DbContextOptionsBuilder<HvoV9DbContext>(options)
            .AddInterceptors(interceptors).Options);
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // The name is generated here from a validated nonce; never supplied by application config.
        command.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }
}
