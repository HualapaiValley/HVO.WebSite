# HVO.Hardware.JkBms

JK BMS battery monitor collector for Hualapai Valley Observatory. Runs as a standalone ASP.NET Core / Blazor Server application. Connects to JK BMS devices over Bluetooth LE, polls cell data, detects alarm transitions and configuration changes, stores readings in a local SQLite outbox, and forwards them to the HVO website API.

## Purpose

- Poll multiple JK BMS devices over Bluetooth LE on a configurable interval
- Decode cell voltages, state of charge (SOC), temperatures, current, and power
- Detect alarm bit transitions (raise/clear) and emit alarm events
- Detect configuration and device-info changes via hash-based deduplication
- Store all readings in a local SQLite outbox before forwarding (survives API downtime)
- Forward readings to the HVO website via `POST /api/v1/bms/readings` with exponential-backoff retry
- Provide a local Blazor SSR web UI for monitoring all connected BMS devices

## Technologies

- .NET 10 / ASP.NET Core
- Blazor Server (SSR) for the local admin UI
- SQLite via Entity Framework Core (outbox database)
- Bluetooth LE via system `hciconfig`/`hci0` adapter

## UI Pages

| Page | Route | Description |
|------|-------|-------------|
| Status | `/` | Real-time list of all devices with cell voltages, SOC, power, and alarms |
| Devices | `/devices` | Configured device list with poll state, error count, backoff, and last poll time |
| Device Detail | `/device/{address}` | Per-device detail with time-series readings and alarm event log |

## Device Configuration

Devices are configured in `appsettings.json` under the `JkBms:Devices` array:

```json
{
  "JkBms": {
    "Devices": [
      { "Address": "C8:47:8C:E4:58:37", "Alias": "bank-1a", "Enabled": true }
    ]
  }
}
```

## Configuration

| Section | Key | Description |
|---------|-----|-------------|
| `JkBms` | `HciAdapter` | Bluetooth adapter name (default `hci0`) |
| `JkBms` | `ConnectTimeoutSeconds` | BLE connection timeout per device |
| `JkBms` | `DefaultPollIntervalSeconds` | How often each device is polled |
| `JkBms` | `Devices` | Array of BMS devices (address, alias, enabled) |
| `Outbox` | `ApiEndpoint` | HVO website ingest URL: `http://<host>/api/v1/bms/readings` |
| `Outbox` | `ApiKey` | Raw API key for `ingest:bms` scope (set via environment or user secrets) |
| `Outbox` | `MaxRetryAttempts` | Max retry attempts per outbox record (default 10) |
| `Outbox` | `MaxBackoffSeconds` | Exponential backoff cap in seconds (default 300) |
| `Outbox` | `SweepIntervalSeconds` | How often the forwarder sweeps pending records (default 5) |
| `Outbox` | `DbPath` | Path to the SQLite outbox database (defaults to `outbox.db` in content root) |

## Outbox

All readings are written to a local SQLite database before being forwarded. The `ForwarderCoordinator` background worker sweeps pending records and fans out to all registered forwarders. A record is marked `Sent` only after all forwarders report success. Failed attempts use exponential backoff.

## Running Locally

```bash
cd src/HVO.Hardware.JkBms
dotnet run
```

The UI is available at `http://localhost:5100` (or the port configured in `appsettings.Development.json`).

> **Note**: Bluetooth LE polling requires a physical Bluetooth adapter and will fail in dev containers or environments without Bluetooth hardware. The UI and outbox logic run regardless; only the BMS poll worker will log connection errors.
