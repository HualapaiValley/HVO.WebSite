using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Outbox;

public sealed class EdgeOutboxInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<EdgeOutboxOptions> options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var parentDirectory = Path.GetDirectoryName(configured.DatabasePath)
            ?? throw new InvalidOperationException("Outbox:DatabasePath must have a parent directory.");
        Directory.CreateDirectory(parentDirectory);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>();
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
            db,
            configured.EffectivePayloadTypes[0],
            configured.PayloadVersion,
            cancellationToken).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
