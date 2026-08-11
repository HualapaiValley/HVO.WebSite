using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Outbox;

var builder = WebApplication.CreateBuilder(args);

var testDataRoot = Path.Combine(builder.Environment.ContentRootPath, "TestData");
var databasePath = builder.Configuration["HVO_EDGE_TEST_DATABASE"]
    ?? Path.Combine(Path.GetTempPath(), "hvo-edge-test-host", Environment.ProcessId.ToString(), "outbox.db");
var defaults = new Dictionary<string, string?>
{
    [EdgeConfigurationBuilderExtensions.ConfigurationFileEnvironmentVariable] = Path.Combine(testDataRoot, "gateway.json"),
    ["Edge:Paths:ConfigurationFile"] = Path.Combine(testDataRoot, "gateway.json"),
    ["Edge:Paths:ConfigDirectory"] = testDataRoot,
    ["Edge:Paths:DataDirectory"] = Path.GetDirectoryName(databasePath),
    ["Edge:Paths:SecretsDirectory"] = Path.Combine(testDataRoot, "secrets"),
    ["Outbox:DatabasePath"] = databasePath,
    ["Outbox:PayloadType"] = "test.observation",
    ["Outbox:PayloadVersion"] = "1",
    ["Outbox:SweepIntervalSeconds"] = "1"
};
builder.Configuration.AddInMemoryCollection(defaults.Where(pair => builder.Configuration[pair.Key] is null));

builder.AddHvoEdgeRuntime();
builder.Services.AddSingleton<IEdgeOutboxBatchSender, TestOutboxBatchSender>();
builder.Services.AddSingleton<TestAcquisitionState>();
builder.Services.AddSingleton<IEdgeDiagnosticsSnapshotProvider, TestDiagnosticsSnapshotProvider>();
builder.Services.AddHostedService<TestAcquisitionWorker>();

var app = builder.Build();
app.MapHvoEdgeRuntimeEndpoints();
app.Run();

public partial class Program;

public sealed class TestAcquisitionState
{
    private int _ticks;
    private int _sentRecords;
    public int Ticks => Volatile.Read(ref _ticks);
    public int SentRecords => Volatile.Read(ref _sentRecords);
    internal void Tick() => Interlocked.Increment(ref _ticks);
    internal void RecordSent(int count) => Interlocked.Add(ref _sentRecords, count);
}

internal sealed class TestAcquisitionWorker(TestAcquisitionState state, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            state.Tick();
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeProvider, stoppingToken);
        }
    }
}

internal sealed class TestOutboxBatchSender(TestAcquisitionState state) : IEdgeOutboxBatchSender
{
    public Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
        IReadOnlyList<EdgeOutboxRecord> records,
        CancellationToken cancellationToken)
    {
        state.RecordSent(records.Count);
        return Task.FromResult<IReadOnlyList<EdgeOutboxSendOutcome>>(
            records.Select(record => new EdgeOutboxSendOutcome(record.Id, EdgeOutboxSendStatus.Sent)).ToArray());
    }
}

internal sealed class TestDiagnosticsSnapshotProvider(TestAcquisitionState state) : IEdgeDiagnosticsSnapshotProvider
{
    public ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var started = state.Ticks > 0;
        return ValueTask.FromResult(new EdgeDiagnosticsSnapshot(
            new HVO.Edge.Contracts.GatewayHealthSnapshot(
                started ? HVO.Edge.Contracts.GatewayHealthState.Healthy : HVO.Edge.Contracts.GatewayHealthState.Warning,
                DateTime.UtcNow,
                [],
                started ? HVO.Edge.Contracts.GatewaySampleState.Live : HVO.Edge.Contracts.GatewaySampleState.Waiting,
                "current",
                "idle"),
            new HVO.Edge.Contracts.GatewayDeviceCounts(1, started ? 1 : 0, 0, started ? 0 : 1)));
    }
}
