# Archived RabbitMQ / Service Bus Ingest POC

Status: validated and deferred.

This archive preserves the proof-of-concept artifacts for the brokered ingest design:

```text
Davis collector -> RabbitMQ -> Azure Service Bus -> Azure Functions -> Azure SQL
```

The live POC proved the path works for `hvo.weather.raw.v1`, but the design was deferred because it adds more operational complexity than the current observatory workload needs.

Use this archive only as reference material. Files under this directory are not active production code and are intentionally not referenced by `HVO.WebSite.sln`.

Contents:

| Path | Purpose |
|------|---------|
| `docs/` | POC design notes, validation result, caveats, and rollback notes |
| `deploy/observatory/` | RabbitMQ compose/topology/sample-message artifacts used during the POC |
| `scripts/` | Azure Service Bus and RabbitMQ setup scripts used during the POC |
| `src/HVO.Ingest.Contracts/` | Archived canonical envelope/contracts project |
| `src/HVO.Ingest.Functions/` | Archived Service Bus-triggered Azure Functions project |
| `src/HVO.Hardware.DavisVantagePro2/Ingest/` | Archived optional RabbitMQ publisher prototype for Davis |

Repository cleanup completed after validation:

- Active Davis collector code reverted to the existing SQLite outbox + website API path.
- POC code, scripts, deploy files, and notes moved under this archive.

Cloud cleanup completed after validation:

- Temporary POC Function App deleted.
- Temporary POC Function storage account deleted.
- Temporary Service Bus listen policy deleted.
- Temporary Service Bus topic and subscriptions deleted.
- Temporary Service Bus topic send policy deleted with the topic.

The preferred near-term direction is source/provider-specific lightweight edge apps with local SQLite outboxes and typed website API ingest endpoints.
