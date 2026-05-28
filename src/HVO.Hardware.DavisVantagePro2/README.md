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
- MudBlazor for the shell, navigation, layout primitives, and form controls
- HVO.WebSite.Themes for shared fonts and HVO theme assets
- HVO.Staging shared astronomy/domain helpers used by the live dashboard
- SQLite via Entity Framework Core (outbox database)
- TCP socket client for Davis serial protocol

## Key Dependencies

| Dependency | Source | Role |
|------------|--------|------|
| `MudBlazor` | NuGet | App shell, app bars, layout primitives, buttons, stacks, papers |
| `Microsoft.EntityFrameworkCore.Sqlite` | NuGet | Durable local outbox storage |
| `Serilog.*` | NuGet | Structured logging to console, files, and OTLP |
| `OpenTelemetry.*` | NuGet | Trace and metrics export |
| `HVO.Enterprise.Telemetry*` | NuGet | Shared HVO telemetry wiring |
| `HVO.WebSite.Themes` | Project reference | Shared fonts and theme assets |
| `HVO.Staging` | Project reference | Astronomy helpers and shared staging-domain code |

## UI Pages

| Page | Route | Description |
|------|-------|-------------|
| Status | `/`, `/status-live` | Live weather dashboard and baseline shell/template implementation |
| Archive | `/archive` | Set archive interval, clear memory, fetch and browse history |
| Archive Browser | `/archive-browser` | Browse archived records and history views |
| Calibration | `/calibration` | Adjust temperature, humidity, and wind calibration offsets |
| Rain Settings | `/rain` | Configure rain bucket type, reset rain year counter |
| Barometer | `/barometer` | Display barometer trend, adjust offset |
| Settings | `/settings` | Display console configuration |
| Console Settings | `/console-settings` | Console settings workspace and configuration details |
| Application Settings | `/application-settings` | Application-specific service configuration |
| Transmitters | `/transmitters` | View and edit transmitter sensor assignments |
| Station Info | `/info` | Firmware version, model, serial number, latitude, longitude |
| Station Diagnostics | `/station-info` | Station diagnostics and connection details |
| Clock | `/clock` | Sync console clock to UTC |
| Reception | `/reception` | RF signal strength histogram by channel |
| Alarms | `/alarms` | Alarm definitions and active alarm management |

## Dashboard Template Baseline

The Status page is now the reference implementation for the hardware admin shell. Future Davis pages, and likely the other hardware apps, should follow this structure instead of introducing page-specific layout systems.

- `Components/Layout/MainLayout.razor` owns the fixed top app bar, bottom footer bar, and the central content canvas.
- `Components/Layout/ShellLayoutState.cs` is the shared UI state container for theme mode, page title/summary, and explicit footer slot status.
- `wwwroot/app.css` owns shell spacing, canvas scrolling behavior, shell theme classes, and the shared dashboard color tokens for dark and light mode.
- Page components such as `Components/Pages/Status.razor` provide the actual content, while scoped files such as `Components/Pages/Status.razor.css` define page-local layout without overriding the shell contract.

### How It Works

1. `MainLayout` applies either `shell-theme-dark` or `shell-theme-light` based on `ShellLayoutState.IsDarkMode`.
2. `app.css` defines the shell variables plus dashboard-specific tokens, so the page can switch themes without duplicating markup.
3. Each page updates the shell header and footer through `ShellLayoutState.SetPage(...)` and `SetFooter(...)`.
4. The center canvas handles scrolling, which keeps the top and bottom bars pinned while large page content remains scrollable.
5. Page-specific CSS consumes the shared variables with `var(...)` tokens rather than hardcoding theme colors.

### Status Page Notes

- The live charts are custom inline SVG charts generated from `Status.razor.cs`; there is no LiveCharts dependency.
- History samples are bucketed and trimmed to keep the 24-hour charts bounded instead of continuously widening.
- The astronomy card uses shared astronomy calculations and a computed SVG `viewBox` so the graphic stays tightly cropped.
- Footer indicators are explicit state values, not inferred from display strings.

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
