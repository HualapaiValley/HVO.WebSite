using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Hosting;

internal sealed class EdgeRuntimeInitializer(
    EdgeDiagnosticsCredential credential,
    IOptions<EdgePathOptions> paths,
    IOptions<EdgeOutboxOptions> outbox) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _ = credential;
        if (!EdgePathOptionsValidator.IsWithin(outbox.Value.DatabasePath, paths.Value.DataDirectory))
            throw new OptionsValidationException(
                EdgeOutboxOptions.SectionName,
                typeof(EdgeOutboxOptions),
                ["Outbox:DatabasePath must remain under Edge:Paths:DataDirectory."]);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
