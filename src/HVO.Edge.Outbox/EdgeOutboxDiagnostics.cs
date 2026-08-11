using HVO.Edge.Contracts;

namespace HVO.Edge.Outbox;

public sealed class EdgeOutboxDiagnostics(DefaultEdgeOutboxDbContext db)
{
    public Task<GatewayOutboxDiagnostics> ReadAsync(CancellationToken cancellationToken = default) =>
        EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken);
}
