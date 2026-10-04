# HVO.Edge.Contracts

Active .NET 10 shared library for typed telemetry/CloudEvents envelopes, payload
versions, weather/power records and gateway health/diagnostics authorization.
It has an ASP.NET Core framework reference and no project or NuGet package
dependencies. There is no process startup or configuration/secret loader.

Start with [TelemetryEnvelope](TelemetryEnvelope.cs),
[CloudEventsEnvelope](CloudEventsEnvelope.cs), [payload types](EdgePayloadTypes.cs),
[Weather](Weather/) and [PowerSystem](PowerSystem/). Producers/consumers own
source/time identity, units, secrets and compatibility; this library does not
grant acquisition or source authority.

After exact-SDK locked root restore/build:

```bash
dotnet test tests/HVO.Edge.Contracts.Tests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

[Headless runtime](../../docs/architecture/EDGE_VNEXT_RUNTIME.md),
[data flows](../../docs/architecture/EDGE_DATA_FLOWS.md) and
[tests](../../tests/README.md#hvoedgecontractstests) own integration details.
