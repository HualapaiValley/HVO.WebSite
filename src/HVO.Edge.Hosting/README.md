# HVO.Edge.Hosting

Active .NET 10 shared host library depending on [Contracts](../HVO.Edge.Contracts/README.md)
and [Outbox](../HVO.Edge.Outbox/README.md), resilient HTTP, Serilog and OpenTelemetry.
[AddHvoEdgeRuntime](EdgeWebApplicationBuilderExtensions.cs) composes identity,
mounted configuration, startup secrets/auth, structured logging, telemetry,
runtime initialization/outbox and named resilient HTTP. A gateway still owns
device acquisition, sender mapping and its diagnostics snapshot provider.

[Mounted configuration](Configuration/EdgeConfigurationBuilderExtensions.cs)
selects `HVO_EDGE_CONFIG_FILE` or `Edge:Paths:ConfigurationFile`, default
`/app/config/gateway.json`, under an absolute config directory. Production
requires the file; reload is disabled. Environment overrides are reapplied after
the file. [Edge:Paths](Configuration/EdgePathOptions.cs) defaults to config
`/app/config`, data `/app/data`, secrets `/run/secrets`.
[Edge:Runtime](EdgeRuntimeOptions.cs) owns service/gateway/source/site/device
identity and DiagnosticsApiKeySecret. Secret references resolve mounted files at
startup; never place raw credentials in ordinary gateway JSON.

[Endpoint mapping](Diagnostics/EdgeDiagnosticsEndpointRouteBuilderExtensions.cs)
adds public process liveness and gateway snapshot health/readiness; critical
health is 503. Protected health/outbox/status require the configured diagnostics
key; runtime outbox settings have a bounded authenticated override endpoint.
There is no local dashboard or generic device-control API.

After exact-SDK locked root restore/build:

```bash
dotnet test tests/HVO.Edge.Hosting.Tests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

Tests use the [test-only real host](../../tests/README.md#hvoedgehostingtesthost);
no physical collector is required. Canonical [runtime](../../docs/architecture/EDGE_VNEXT_RUNTIME.md),
[operations/auth](../../docs/GATEWAY_OPERATIONS.md) and
[secret prerequisites](../../docs/development/key-vault-materialization.md) own deeper procedures.
