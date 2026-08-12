using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Outbox.Tests;

[TestClass]
public sealed class EdgeOutboxOptionsAndRegistrationTests
{
    [TestMethod]
    public void Defaults_MatchVNextRuntimeConventions()
    {
        var options = new EdgeOutboxOptions();

        options.DatabasePath.Should().Be("/app/data/outbox.db");
        options.PayloadType.Should().Be("edge.telemetry");
        options.PayloadTypes.Should().BeEmpty();
        options.EffectivePayloadTypes.Should().Equal("edge.telemetry");
        options.PayloadVersion.Should().Be("1");
        options.BatchSize.Should().Be(50);
        options.SweepIntervalSeconds.Should().Be(5);
        options.MaxRetryAttempts.Should().Be(10);
        options.MaxBackoffSeconds.Should().Be(300);
        options.SentRetentionDays.Should().Be(7);
        options.FailedRetentionDays.Should().Be(30);
    }

    [TestMethod]
    public void Options_AcceptBoundedPayloadTypeListWithoutChangingSingleTypeCompatibility()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Outbox:DatabasePath"] = Path.Combine(Path.GetTempPath(), "outbox.db"),
            ["Outbox:PayloadTypes:0"] = "weather.live",
            ["Outbox:PayloadTypes:1"] = "weather.archive",
        });

        provider.GetRequiredService<IOptions<EdgeOutboxOptions>>().Value.EffectivePayloadTypes
            .Should().Equal("weather.live", "weather.archive");
    }

    [TestMethod]
    public void Options_RejectRelativeDatabasePathAndOutOfRangeValues()
    {
        using var relativeProvider = CreateProvider(new Dictionary<string, string?>
        {
            ["Outbox:DatabasePath"] = "data/outbox.db",
        });
        using var boundsProvider = CreateProvider(new Dictionary<string, string?>
        {
            ["Outbox:DatabasePath"] = Path.Combine(Path.GetTempPath(), "outbox.db"),
            ["Outbox:BatchSize"] = "0",
        });

        var relative = () => relativeProvider.GetRequiredService<IOptions<EdgeOutboxOptions>>().Value;
        var bounds = () => boundsProvider.GetRequiredService<IOptions<EdgeOutboxOptions>>().Value;

        relative.Should().Throw<OptionsValidationException>().WithMessage("*absolute path*");
        bounds.Should().Throw<OptionsValidationException>().WithMessage("*BatchSize*");
    }

    [TestMethod]
    public async Task Registration_UsesConfiguredPathAndInitializerCreatesSchemaBeforeWorkers()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "nested", "outbox.db");
            await using var provider = CreateProvider(new Dictionary<string, string?>
            {
                ["Outbox:DatabasePath"] = databasePath,
                ["Outbox:PayloadType"] = "weather.reading",
                ["Outbox:PayloadVersion"] = "2",
            });
            var hostedServices = provider.GetServices<IHostedService>().ToList();
            var initializer = hostedServices.OfType<EdgeOutboxInitializer>().Single();
            var forwarder = hostedServices.OfType<EdgeOutboxForwarder>().Single();

            hostedServices.IndexOf(initializer).Should().BeLessThan(hostedServices.IndexOf(forwarder));
            Directory.Exists(Path.GetDirectoryName(databasePath)).Should().BeFalse();

            await initializer.StartingAsync(CancellationToken.None);

            File.Exists(databasePath).Should().BeTrue();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>();
            (await db.Database.CanConnectAsync()).Should().BeTrue();
            (await EdgeOutboxSchemaValidator.ValidateAsync(db)).IsCompatible.Should().BeTrue();
            scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>().Should().NotBeNull();
            scope.ServiceProvider.GetRequiredService<EdgeOutboxDiagnostics>().Should().NotBeNull();
            provider.GetRequiredService<RuntimeOutboxSettings>().Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Records_PersistAcrossFreshProvidersUsingSameDatabasePath()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "outbox.db");
            await using (var first = CreateProvider(Configuration(databasePath)))
            {
                await InitializeAsync(first);
                await using var scope = first.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
                await store.EnqueueAsync(Message(), CancellationToken.None);
            }

            await using (var second = CreateProvider(Configuration(databasePath)))
            {
                await InitializeAsync(second);
                await using var scope = second.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>();
                (await db.OutboxRecords.CountAsync()).Should().Be(1);
                (await db.OutboxRecords.SingleAsync()).PayloadJson.Should().Be("{\"value\":42}");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider CreateProvider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEdgeOutboxBatchSender, NoOpSender>();
        services.AddHvoEdgeOutbox(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static Dictionary<string, string?> Configuration(string databasePath) => new()
    {
        ["Outbox:DatabasePath"] = databasePath,
        ["Outbox:PayloadType"] = "test.reading",
        ["Outbox:PayloadVersion"] = "1",
        ["Outbox:SweepIntervalSeconds"] = "1",
    };

    private static async Task InitializeAsync(IServiceProvider provider)
    {
        var initializer = provider.GetServices<IHostedService>().OfType<EdgeOutboxInitializer>().Single();
        await initializer.StartingAsync(CancellationToken.None);
    }

    private static EdgeOutboxMessage Message() => new(
        "source-1",
        DateTime.Parse("2026-08-11T01:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
        "test.reading",
        "1",
        "{\"value\":42}");

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hvo-edge-outbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoOpSender : IEdgeOutboxBatchSender
    {
        public Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
            IReadOnlyList<EdgeOutboxRecord> records,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EdgeOutboxSendOutcome>>([]);
    }
}
