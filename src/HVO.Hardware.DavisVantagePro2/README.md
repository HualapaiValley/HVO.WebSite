# HVO.Hardware.DavisVantagePro2

Davis Vantage Pro 2 weather station collector for Hualapai Valley Observatory. Runs as a standalone ASP.NET Core / Blazor Server application. Connects to the Davis console over TCP, polls and archives weather readings, stores them in a local SQLite outbox for durability, and forwards them to the HVO website API.

## Purpose

- Maintain a persistent TCP connection to a Davis Vantage Pro 2 console
- Stream LOOP2 packets (~2 sec cadence) for real-time readings
- Collect LOOP1 packets periodically for battery, forecast, and sunrise/sunset data
- Optionally run DMPAFT archive dump on startup to backfill missed records
- Store all readings in a local SQLite outbox before forwarding (survives API downtime)
- Forward readings to the HVO website via `POST /api/v1/weather/raw` with exponential-backoff retry
- Provide a local Blazor SSR web UI for monitoring and console management

## Technologies

- .NET 10 / ASP.NET Core
- Blazor Server (SSR) for the local admin UI
- SQLite via Entity Framework Core (outbox database)
- TCP socket client for Davis serial protocol

## UI Pages

| Page | Route | Description |
|------|-------|-------------|
| Status | `/` | Live LOOP2 packet display, worker state, outbox counts, connectivity |
| Archive | `/archive` | Set archive interval, clear memory, fetch and browse history |
| Calibration | `/calibration` | Adjust temperature, humidity, and wind calibration offsets |
| Rain Settings | `/rain` | Configure rain bucket type, reset rain year counter |
| Barometer | `/barometer` | Display barometer trend, adjust offset |
| Settings | `/settings` | Display console configuration |
| Transmitters | `/transmitters` | View and edit transmitter sensor assignments |
| Station Info | `/info` | Firmware version, model, serial number, latitude, longitude |
| Clock | `/clock` | Sync console clock to UTC |
| Reception | `/reception` | RF signal strength histogram by channel |

## Configuration

| Section | Key | Description |
|---------|-----|-------------|
| `Station` | `Host` | Davis console IP address |
| `Station` | `Port` | TCP port (typically `22222`) |
| `Station` | `StationId` | Station identifier sent with each reading (e.g. `hvo-davis-01`) |
| `Station` | `ArchiveCatchupOnStartup` | Run DMPAFT on startup to backfill missed archive records |
| `Outbox` | `ApiEndpoint` | HVO website ingest URL: `http://<host>/api/v1/weather/raw` |
| `Outbox` | `ApiKey` | Raw API key for `ingest:weather` scope (set via environment or user secrets) |
| `Outbox` | `BatchSize` | Max records per batch POST (default 50) |
| `Outbox` | `MaxRetryAttempts` | Max retry attempts per outbox record (default 10) |
| `Outbox` | `MaxBackoffSeconds` | Exponential backoff cap in seconds (default 300) |

## Outbox

All readings are written to a local SQLite database before being forwarded. The outbox forwarder runs as a background worker, sweeping pending records and POSTing them in batches. Records are marked `Sent` only after a successful `201` response. Failed attempts increment the retry counter with exponential backoff.

## Running Locally

```bash
cd src/HVO.Hardware.DavisVantagePro2
dotnet run
```

The UI is available at `http://localhost:5000` (or the port configured in `appsettings.Development.json`).
