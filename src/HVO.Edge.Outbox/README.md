# HVO.Edge.Outbox

Active .NET 10 durable transport library depending on [Contracts](../HVO.Edge.Contracts/README.md),
EF Core/Relational/SQLite and the ASP.NET Core framework. It is not an application
or permanent domain-history store. Collectors register `AddHvoEdgeOutbox`; the
initializer, scoped store, forwarder and diagnostics share service-owned SQLite.

[Registration](EdgeOutboxServiceCollectionExtensions.cs) binds the `Outbox`
section with startup validation. DatabasePath defaults to `/app/data/outbox.db`;
payload type(s)/version select the service's permitted stream. Callers own the
data directory, sender routing and secret-backed destination. Runtime batch/sweep
overrides are in-memory and reset on restart. Inspect source rather than editing
live SQLite rows manually.

Retry/backoff, permanent failures/dead letters, compaction and RetryExhausted
requeue are distinct. Sender HTTP response accounting remains device-owned;
[operations](../../docs/GATEWAY_OPERATIONS.md) records that matrix.
[Quiescent backup/rollback](../../docs/gateways/sqlite-backup-and-rollback.md)
governs all recovery, including WAL/checkpoint and later observations.

After exact-SDK locked root restore/build:

```bash
dotnet test tests/HVO.Edge.Outbox.Tests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

[Headless runtime](../../docs/architecture/EDGE_VNEXT_RUNTIME.md),
[data flows](../../docs/architecture/EDGE_DATA_FLOWS.md) and
[test owner](../../tests/README.md#hvoedgeoutboxtests) explain composition and fixtures.
