# HVO.WebSite — v9 Architecture & Development Plan

## Overview

HVO.WebSite v9 is a clean-slate rebuild of the Hualapai Valley Observatory website on .NET 10. The legacy `dbo` schema (SQL Server, Azure) remains read-accessible but is **not** the target for new development. All v9 features use a new `v9` schema owned exclusively by EF Core migrations via `HvoV9DbContext`.

---

## Key Architectural Decisions

### Database Strategy

- **No bridge views** — the legacy `dbo` schema is preserved as a reference (see `HVO.Database` SQL project) but v9 code does not build on top of it
- **Clean start** — all new entities live in the `v9` schema; `HvoV9DbContext` owns migrations
- **Dual contexts**: `HvoDbContext` (legacy read-only access to `dbo`) + `HvoV9DbContext` (v9 read/write to `v9` schema)
- **Connection string**: `ConnectionStrings__HualapaiValleyObservatory` environment variable — no hardcoded credentials

### Weather Data Retention

| Granularity | Retention |
|-------------|-----------|
| Raw (every reading) | 2 months |
| Per-minute aggregate | 6 months |
| Hourly aggregate | Forever |

### Authentication

- **Provider**: Microsoft Entra ID (Azure AD) — `Microsoft.Identity.Web` (no third-party auth libraries)
- **Roles**: `User` (read dashboard) and `Admin` (manage site, view logs)
- **Claims**: Role-based with extension points for future custom claims
- No local accounts; all identity managed via Entra

### API Design

- **Ingest endpoints**: Authenticated via API key middleware (header-based); used by weather stations and camera systems
- **Read endpoints**: Authenticated via Entra bearer token (or anonymous for public dashboard)
- **All data access** goes through the API layer — no direct DB calls from Razor pages/components
- RESTful versioned endpoints under `/api/v1/`

---

## v9 Schema Entities (EF Core Migrations)

| Entity | Purpose |
|--------|---------|
| `WeatherRaw` | Raw station readings, 2-month retention |
| `WeatherMinute` | Per-minute aggregates, 6-month retention |
| `WeatherHourly` | Hourly aggregates, retained forever |
| `ImageMetadata` | All-sky camera image records |
| `AlertLog` | System alerts and notifications |
| `SiteConfiguration` | Key/value site settings |

---

## Phase Plan

### Phase 1 — Foundation (Complete)

- [x] Upgrade to .NET 10, zero warnings/errors
- [x] Remove hardcoded credentials; use environment variables
- [x] Remove Playground project
- [x] Audit and snapshot legacy `dbo` schema (`HVO.Database` SQL project)
- [x] Add test projects: unit (MSTest + Moq), API (WebApplicationFactory), E2E (Playwright)
- [x] Create `HvoV9DbContext` with initial EF Core migration
- [x] Define v9 schema entities listed above
- [x] Configure Entra ID integration in `Program.cs` via `Microsoft.Identity.Web`
- [x] Implement API key middleware for ingest endpoints
- [x] Implement base ingest endpoints (`POST /api/v1/weather/raw`)
- [x] Implement base read endpoints (`GET /api/v1/weather/v9/raw/recent`, `GET /api/v1/weather/v9/hourly/recent`)
- [x] Implement Admin and User Entra app roles with `[Authorize(Policy = "AdminOnly")]` protected page
- [x] Integration tests for `/admin` page authorization (unauthenticated → OIDC challenge, wrong role → AccessDenied, Admin role → 200)

---

### Phase 1.1 — Weather Station Data Acquisition

**Goal:** Live weather data flows from the Davis Vantage Pro 2 console → SQLite outbox → web API → database.

#### New project: `src/HVO.Hardware.DavisVantagePro2`

A .NET Worker Service deployed as a Docker container (multi-arch: `linux/amd64` + `linux/arm64` for Raspberry Pi).

**Connection:** WeatherLink IP adapter at `192.168.2.121:22222` — TCP socket tunneling the Davis serial binary protocol. `TcpClient` replaces `SerialPort`; the binary framing (LOOP2, DMPAFT, CRC-CCITT-16) is identical to serial.

**Components:**

| Component | Purpose |
|---|---|
| `DavisConsoleClient` | TCP connection management — wake, send commands, read binary frames, CRC validation |
| `DavisLoop2Packet` | Maps the 99-byte LOOP2 binary structure (little-endian, `struct`-style) |
| `DavisArchiveRecord` | Maps 52-byte archive records from DMPAFT (used for catchup on startup) |
| `DavisProtocolConstants` | CRC table, magic bytes, packet type codes |
| `WeatherStationWorker` | `BackgroundService` — polls LOOP2 every N seconds, runs DMPAFT catchup on start |
| `OutboxForwarder` | `BackgroundService` — sweeps pending outbox records, POSTs to web API, marks sent |
| `OutboxDbContext` | EF Core + SQLite — owns the `OutboxRecord` table |
| `StationStatusController` | Minimal local API: `GET /status` returns latest record + outbox stats |
| Blazor/Razor status page | Single-page local UI: latest reading, station health, pending/sent counts |

**Offline-first outbox (transactional, FIFO, guaranteed delivery):**

```
READ LOOP2 from TCP
  │
  ├─ BEGIN TRANSACTION
  │    INSERT OutboxRecord (payload, status='Pending', sequence_id=AUTOINCREMENT)
  │  COMMIT  ← durable; crash-safe from this point
  │
OutboxForwarder (background sweep):
  │
  ├─ SELECT TOP 1 WHERE status='Pending' ORDER BY sequence_id ASC
  ├─ POST /api/v1/weather/v9/raw  (X-Api-Key header)
  │    on HTTP 2xx  → UPDATE status='Sent', sent_at=now
  │    on failure   → UPDATE attempt_count++, next_retry_at=now+exponential_backoff
  │    (row never deleted — audit trail preserved)
```

No row is deleted or marked sent until HTTP 2xx is confirmed. Process crash → restart picks up from last `Pending` row. Exponential backoff with a configurable max (e.g., 5 minutes) prevents thundering herd on reconnect.

**LOOP2 fields captured → `WeatherRaw` (see schema changes below):**

From the Davis serial protocol spec (verified against WeeWX vantage driver):

| LOOP2 field | Meaning | `WeatherRaw` column |
|---|---|---|
| `outTemp` | Outside temperature (°F × 10, signed) | `TemperatureF` |
| `outHumidity` | Outside relative humidity (%) | `HumidityPercent` |
| `dewpoint` | Dew point (°F, console-computed) | `DewPointF` |
| `heatindex` | Heat index (°F, console-computed) | `HeatIndexF` *(new)* |
| `windchill` | Wind chill (°F, console-computed) | `WindChillF` *(new)* |
| `barometer` | Station-corrected barometric pressure (inHg × 1000) | `BarometricPressureInHg` |
| `windSpeed` | Instantaneous wind speed (mph) | `WindSpeedMph` |
| `windGust10` | 10-min wind gust (mph) | `WindGustMph` |
| `windDir` | Instantaneous wind direction (degrees) | `WindDirectionDegrees` |
| `rainRate` | Instantaneous rain rate (in/hr, decoded from bucket tips) | `RainRateInchesPerHour` *(renamed)* |
| `dayRain` | Cumulative rain since midnight (in, decoded from bucket tips) | `DailyRainInches` *(renamed)* |
| `UV` | UV index (× 10) | `UvIndex` |
| `radiation` | Solar radiation (W/m²) | `SolarRadiationWm2` |
| `inTemp` | Inside console temperature (°F × 10) | `InsideTemperatureF` *(new)* |
| `inHumidity` | Inside console humidity (%) | `InsideHumidityPercent` *(new)* |

> **Note on `RainfallInches` rename:** The legacy field was ambiguous (rate? daily? storm?). Per the Davis serial protocol, `rainRate` is instantaneous rate and `dayRain` is daily cumulative. These become two separate columns. The existing `RainfallInches` column is removed in the migration.

**`WeatherRaw` schema changes required (new EF Core migration):**

- Remove: `RainfallInches`
- Add: `RainRateInchesPerHour`, `DailyRainInches`, `HeatIndexF`, `WindChillF`, `InsideTemperatureF`, `InsideHumidityPercent`
- Add unique index on `(StationId, RecordedAt)` — enables idempotent ingest (DMPAFT catchup retries)
- Ingest endpoint handles unique constraint violation as HTTP 200/idempotent (not 500)

**Local web UI (no auth — LAN-only):**

Single Blazor/Razor page at the container's HTTP port showing:
- Latest reading (all fields, timestamp, age)
- Station connectivity status (connected / last seen)
- Outbox: pending count, last-sent timestamp, last error

**Docker:**

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# multi-arch: --platform linux/amd64 or linux/arm64
```

`docker-compose.yml` for local dev with environment overrides for host IP, API endpoint, API key.

**Configuration (environment variables / appsettings):**

| Setting | Purpose |
|---|---|
| `Davis:Host` | WeatherLink IP address (`192.168.2.121`) |
| `Davis:Port` | TCP port (`22222`) |
| `Davis:PollingIntervalSeconds` | How often to request LOOP2 (default: `10`) |
| `Davis:ArchiveCatchupOnStartup` | Run DMPAFT on startup (`true`) |
| `Api:Endpoint` | Ingest API URL |
| `Api:Key` | API key (with `ingest:weather` scope) |
| `Outbox:MaxRetryAttempts` | Before giving up on a record |
| `Outbox:MaxBackoffSeconds` | Cap for exponential backoff |

**Tests:**

| Test | Type |
|---|---|
| LOOP2 binary packet parsing (known byte sequence → expected field values) | Unit |
| CRC-CCITT-16 validation (valid and corrupt packet) | Unit |
| Rain bucket tip decoding (all 3 bucket types) | Unit |
| Outbox: record inserted on read, not deleted on forward failure | Unit |
| Outbox: record marked sent only on HTTP 2xx | Unit |
| Outbox: FIFO ordering (sequence_id respected) | Unit |
| Outbox: exponential backoff increments correctly | Unit |
| End-to-end: LOOP2 parsed → outbox written → forwarded → `WeatherRaw` in DB | Integration |

#### Web app changes for Phase 1.1

- `WeatherAggregationService` (`BackgroundService` in `HVO.WebSite.v9`):
  - Every 60 seconds: aggregate `WeatherRaw` rows in the last completed minute → upsert `WeatherMinute`
  - Every 60 minutes: aggregate `WeatherMinute` rows in the last completed hour → upsert `WeatherHourly`
  - Idempotent: upsert by `(StationId, PeriodStart)` — safe to re-run

**Aggregation strategy per field** (derived from legacy stored procedures in `dbo`):

| Field | Method | Rationale |
|-------|--------|-----------|
| `TemperatureF` | AVG + MIN + MAX | Range matters for daily extremes |
| `InsideTemperatureF` | AVG | Stable, no extremes needed |
| `HumidityPercent` | AVG | |
| `InsideHumidityPercent` | AVG | |
| `DewPointF` | AVG | Console-computed derived value |
| `HeatIndexF` | AVG | Console-computed derived value |
| `WindChillF` | AVG | Console-computed derived value |
| `BarometricPressureInHg` | AVG | |
| `WindSpeedMph` | AVG + MIN + MAX | Range captures calm and peak |
| `WindGustMph` | MAX | Highest gust in the period is the meaningful value |
| `WindDirectionDegrees` | Vector average (sin/cos) | Scalar average is meaningless across 0°/360° boundary |
| `RainRateInchesPerHour` | AVG | Instantaneous rate |
| `DailyRainInches` | MAX | Running cumulative total from console — last (highest) value in period |
| `SolarRadiationWm2` | AVG | |
| `UvIndex` | AVG | |

> **Note:** `WeatherMinute` and `WeatherHourly` entities will need additional columns to hold min/max alongside avg (e.g., `TemperatureFMin`, `TemperatureFMax`, `WindSpeedMphMin`, `WindSpeedMphMax`). If there are questions about how any specific field should roll up, ask before implementing.

**Phase 1.1 checklist:**

- [ ] `WeatherRaw` schema migration — add/rename rainfall and inside sensor fields, unique index
- [ ] Update ingest endpoint to handle unique constraint as idempotent (200 if duplicate)
- [x] Create `src/HVO.Hardware.DavisVantagePro2` Worker Service project
- [x] Implement `DavisConsoleClient` (TCP, wake, LOOP2, DMPAFT, CRC-CCITT-16)
- [x] Implement `DavisLoop2Packet` binary parser
- [x] Implement `DavisArchiveRecord` binary parser
- [x] Implement SQLite transactional outbox (`OutboxDbContext`, `OutboxRecord`)
- [x] Implement `WeatherStationWorker` (poll LOOP2, write to outbox)
- [x] Implement `OutboxForwarder` (sweep outbox, POST to API, exponential backoff)
- [x] Local status page (latest reading + outbox health)
- [x] Dockerfile (multi-arch: amd64 + arm64)
- [ ] Implement `WeatherAggregationService` in web app (minute + hourly rollups)
- [x] Unit tests: packet parsing, CRC, rain decoding (`Loop2PacketTests`, `CrcCalculatorTests`, `ArchiveRecordTests`)
- [ ] Unit tests: outbox behavior (insert-on-read, no-delete-on-failure, sent-on-2xx, FIFO, backoff)
- [ ] Integration tests: ingest duplicate → idempotent; aggregation correctness
- [x] Zero warnings, zero errors

---

### Phase 1.5 — Data Retention

- [ ] Background job: prune `WeatherRaw` rows older than 2 months
- [ ] Background job: prune `WeatherMinute` rows older than 6 months
- [ ] Tests: retention jobs delete correct rows, leave in-window rows intact

---

### Phase 2 — Weather Dashboard

- [ ] Weather summary page (current conditions, 24h trend)
- [ ] Historical weather charts (hourly/daily/monthly)
- [ ] Image ingest endpoint (`POST /api/v1/images`)
- [ ] Weather station health/status indicator

### Phase 1.2 — JK BMS Battery Monitoring

**Goal:** Live battery data flows from JK BMS units (BLE) → SQLite outbox → (future) web API for storage and dashboard display.

#### New project: `src/HVO.Hardware.JkBms`

A .NET Worker Service deployed as a Docker container (multi-arch: `linux/amd64` + `linux/arm64` for Raspberry Pi). Connects to JK BMS units via Bluetooth LE using the host BlueZ stack (D-Bus socket passthrough — same mechanism as the devcontainer).

**Connection:** BLE UART-over-BLE profile:
- Service UUID: `0000FFE0-0000-1000-8000-00805F9B34FB`
- Characteristic UUID: `0000FFE1-0000-1000-8000-00805F9B34FB` (Write + Notify)
- Request: fixed 20-byte command frame (`AA 55 90 EB 96 …`); response: ~300 bytes across multiple 20-byte BLE notifications requiring reassembly.
- Library: `InTheHand.BluetoothLE` — cross-platform .NET BLE via BlueZ D-Bus.

**BLE Connection Strategy (no persistent connections):**

BLE adapters support only ~3–7 simultaneous connections. With 7+ BMS units, persistent connections are not viable:
- Connect → request → read notifications → reassemble → disconnect per poll cycle.
- `SemaphoreSlim(MaxConcurrentConnections)` limits simultaneous BLE sessions (configurable, default 2).
- Each device maintains its own `NextPollAt` timestamp; a single `BmsPollerWorker` round-robins in priority order.

**Wake-up / Retry Strategy:**

JK BMS units are known to sleep and be unreliable to connect on the first attempt:
- Inner retry loop: 3 attempts × 5 s connect timeout, 2 s pause between attempts (within one poll cycle).
- Before each attempt: 1-second targeted BLE scan to wake the sleeping peripheral.
- If all inner retries fail: mark device as failed, apply per-device exponential backoff (1 → 2 → 5 → 10 min cap).
- Semaphore held only during the active connection, not during inter-attempt delays.
- All connections closed in `try/finally` to prevent handle leaks.

**Components:**

| Component | Purpose |
|---|---|
| `JkBmsClient` | BLE connect/poll/disconnect lifecycle; inner wake-up retry loop |
| `IBmsTransport` | Abstraction over BLE I/O — enables unit testing without hardware |
| `JkBmsBluetoothTransport` | Real `InTheHand.BluetoothLE` implementation |
| `JkBmsProtocol` | Build command frames; reassemble chunked BLE notifications; validate frame; parse |
| `Crc32Calculator` | CRC32 (IEEE 802.3) used for response frame validation |
| `CellInfoPacket` | Parsed cell info: voltages (variable count), pack V/I/SOC, temps, alarms |
| `DeviceInfoPacket` | Parsed device info: firmware version, device name, capacity |
| `BmsDeviceReading` | Domain model written to the SQLite outbox (JSON payload) |
| `IBmsAlarmHandler` + `NullAlarmHandler` | Hook for V2 alarm actions; V1 is a no-op |
| `BmsPollerWorker` | `BackgroundService` — round-robin scheduler, semaphore, per-device backoff |
| `ForwarderCoordinator` | `BackgroundService` — drains outbox to all registered `IReadingForwarder`s |
| `IReadingForwarder` / `HttpApiForwarder` | Fan-out forwarder interface; V1 ships HTTP; future: RabbitMQ, MQTT |
| `OutboxDbContext` | EF Core + SQLite — same pattern as Davis |
| Blazor status page | Live grid: device alias, cell count, SOC, pack voltage, delta mV, last seen, errors |
| Blazor devices page | Configured device list with per-device status and last error |

**Fan-out forwarder architecture:**

```
BmsPollerWorker → OutboxRecord (SQLite, Pending)
                                │
                  ForwarderCoordinator (background sweep)
                                │
                    ┌───────────┴────────────┐
              HttpApiForwarder          (future: MqttForwarder, RabbitMqForwarder, …)
```

Each forwarder is independently registered (`IReadingForwarder`) and maintains its own delivery state. V1 ships `HttpApiForwarder` but it is disabled until the API endpoint is configured (sends nothing; outbox fills and holds).

**JK BMS frame format:**

```
Request (CELL_INFO):   AA 55 90 EB  96 00 00 00  00 00 00 00  00 00 00 00  00 00 00 11
Request (DEVICE_INFO): AA 55 90 EB  97 00 00 00  00 00 00 00  00 00 00 00  00 00 00 11

Response SOF:  55 AA EB 90
Byte 4:        frame_type  (0x01=device_info, 0x02=cell_info)
Byte 5:        frame_counter
Byte 6-7:      data_length (LE uint16)
Byte 8+:       data payload (variable)
Last 4 bytes:  CRC32 (LE uint32, IEEE 802.3, covers SOF through end of data)
```

Response arrives as a stream of 20-byte BLE notifications; the transport layer accumulates and reassembles before returning the complete frame.

**Cell info data layout** (offsets relative to data start, byte 8 of full frame):

| Offset | Length | Field |
|---|---|---|
| 0x00 | 48 bytes | 24 × cell voltage (uint16 LE, mV) — unused cells = 0 |
| 0x30 | 1 | `CellCount` (uint8) |
| 0x31 | 2 | `AverageCellVoltage` (uint16 LE, mV) |
| 0x33 | 2 | `DeltaCellVoltage` (uint16 LE, mV) |
| 0x35 | 1 | `MaxVoltageCellIndex` (uint8, 0-based) |
| 0x36 | 1 | `MinVoltageCellIndex` (uint8, 0-based) |
| 0x37 | 2 | `BalancingCurrentMa` (uint16 LE, mA/10) |
| 0x39 | 1 | `BalancingActive` (uint8: 0=none, 1=active) |
| 0x3A | 2 | `PowerTubeTemperature` (uint16 LE, decoded as `(raw-2731)/10` °C) |
| 0x3C | 2 | `BatteryTemperature1` (same encoding) |
| 0x3E | 2 | `BatteryTemperature2` (same encoding) |
| 0x40 | 4 | `TotalVoltage` (uint32 LE, mV) |
| 0x44 | 4 | `Current` (int32 LE, mA — negative = charging) |
| 0x48 | 2 | `StateOfCharge` (uint16 LE, %) |
| 0x4A | 4 | `RemainingCapacity` (uint32 LE, mAh) |
| 0x4E | 4 | `NominalCapacity` (uint32 LE, mAh) |
| 0x52 | 4 | `CycleCount` (uint32 LE) |
| 0x56 | 4 | `CycleCapacity` (uint32 LE, mAh) |
| 0x5A | 2 | `StateOfHealth` (uint16 LE, %) |
| 0x70 | 4 | `AlarmBitmask` (uint32 LE — raw flags, preserved for V2 event handling) |

> **Note:** Exact offsets and temperature encoding are based on the JK BMS BLE protocol (version 0.10.x as documented by esphome-jk-bms). Minor variations exist across firmware versions. All fields are validated against live hardware in the live test suite.

**Configuration:**

```json
"JkBms": {
  "MaxConcurrentConnections": 2,
  "ConnectTimeoutSeconds": 5,
  "ConnectRetryAttempts": 3,
  "ConnectRetryDelaySeconds": 2,
  "DefaultPollIntervalSeconds": 60,
  "HciAdapter": "hci0",
  "Devices": [
    { "Address": "C8:47:8C:E4:58:37", "Alias": "battery-bank-1a", "PollIntervalSeconds": 60 },
    { "Address": "C8:47:8C:E4:56:B0", "Alias": "battery-bank-1b" },
    …
  ]
}
```

**Tests:**

| Test | Type |
|---|---|
| CRC32 known vectors (empty, single byte, multi-byte, round-trip) | Unit |
| Command frame encoding (cell_info and device_info match expected bytes) | Unit |
| SOF validation (valid, wrong bytes, too short) | Unit |
| Frame reassembly from chunked notifications (1 chunk, many chunks, incomplete) | Unit |
| CRC32 frame validation (valid, corrupted data, corrupted CRC byte) | Unit |
| `CellInfoPacket` parse — 15-cell device, all fields | Unit |
| `CellInfoPacket` parse — 20-cell device, variable cell list | Unit |
| `CellInfoPacket` temperature decoding (known raw → expected °C) | Unit |
| `CellInfoPacket` current sign (positive=discharging, negative=charging) | Unit |
| `DeviceInfoPacket` parse — firmware version, name | Unit |
| `JkBmsClient` — successful connect+poll via `FakeBmsTransport` | Integration |
| `JkBmsClient` — inner retry on first two connect failures, success on third | Integration |
| `JkBmsClient` — all inner retries exhausted → `JkBmsConnectException` | Integration |
| `JkBmsClient` — cancellation mid-connect | Integration |
| `JkBmsClient` — disconnect always called (even on parse failure) | Integration |
| Live: connect to real device, verify cell count matches known device type | Live |
| Live: all cell voltages physically plausible (2.5–3.65 V range) | Live |
| Live: pack voltage = sum of cell voltages (within 1%) | Live |
| Live: SOC in [0, 100] range | Live |
| Live: temperatures in [-20, 80] °C range | Live |

**Phase 1.2 checklist:**

- [x] `docs/PLAN.md` — JK BMS phase documented
- [x] `Directory.Packages.props` — `InTheHand.BluetoothLE` version pinned
- [x] `HVO.WebSite.sln` — JK BMS main + test projects added
- [x] `src/HVO.Hardware.JkBms/HVO.Hardware.JkBms.csproj`
- [x] `src/HVO.Hardware.JkBms/AssemblyInfo.cs`
- [x] `src/HVO.Hardware.JkBms/Program.cs`
- [x] `src/HVO.Hardware.JkBms/appsettings.json` + `appsettings.Development.json`
- [x] `src/HVO.Hardware.JkBms/Dockerfile`
- [x] `src/HVO.Hardware.JkBms/Configuration/JkBmsOptions.cs`
- [x] `src/HVO.Hardware.JkBms/Configuration/BmsDeviceConfig.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/Crc32Calculator.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/JkBmsProtocol.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/JkBmsExceptions.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/Packets/CellInfoPacket.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/Packets/DeviceInfoPacket.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/Transport/IBmsTransport.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/Transport/IBmsTransportFactory.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/Transport/JkBmsBluetoothTransport.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/Transport/JkBmsBluetoothTransportFactory.cs`
- [x] `src/HVO.Hardware.JkBms/Protocol/JkBmsClient.cs`
- [x] `src/HVO.Hardware.JkBms/Bms/BmsDeviceReading.cs`
- [x] `src/HVO.Hardware.JkBms/Bms/IBmsAlarmHandler.cs`
- [x] `src/HVO.Hardware.JkBms/Outbox/OutboxRecord.cs`
- [x] `src/HVO.Hardware.JkBms/Outbox/OutboxDbContext.cs`
- [x] `src/HVO.Hardware.JkBms/Outbox/OutboxOptions.cs`
- [x] `src/HVO.Hardware.JkBms/Outbox/Forwarders/IReadingForwarder.cs`
- [x] `src/HVO.Hardware.JkBms/Outbox/Forwarders/HttpApiForwarder.cs`
- [x] `src/HVO.Hardware.JkBms/Workers/BmsPollerWorker.cs`
- [x] `src/HVO.Hardware.JkBms/Workers/ForwarderCoordinator.cs`
- [x] `src/HVO.Hardware.JkBms/Components/App.razor`
- [x] `src/HVO.Hardware.JkBms/Components/Routes.razor`
- [x] `src/HVO.Hardware.JkBms/Components/_Imports.razor`
- [x] `src/HVO.Hardware.JkBms/Components/Layout/MainLayout.razor`
- [x] `src/HVO.Hardware.JkBms/Components/Pages/Status.razor` + `.razor.cs`
- [x] `src/HVO.Hardware.JkBms/Components/Pages/Devices.razor` + `.razor.cs`
- [x] `tests/HVO.Hardware.JkBms.Tests/HVO.Hardware.JkBms.Tests.csproj`
- [x] `tests/HVO.Hardware.JkBms.Tests/Protocol/Crc32CalculatorTests.cs`
- [x] `tests/HVO.Hardware.JkBms.Tests/Protocol/JkBmsProtocolTests.cs`
- [x] `tests/HVO.Hardware.JkBms.Tests/Protocol/CellInfoPacketTests.cs`
- [x] `tests/HVO.Hardware.JkBms.Tests/Protocol/DeviceInfoPacketTests.cs`
- [x] `tests/HVO.Hardware.JkBms.Tests/Fakes/FakeBmsTransport.cs`
- [x] `tests/HVO.Hardware.JkBms.Tests/Integration/JkBmsClientTests.cs`
- [x] `tests/HVO.Hardware.JkBms.Tests/Live/LiveBmsTests.cs`
- [ ] Zero build warnings, zero build errors
- [ ] Zero test failures

---

### Phase 3 — Power Dashboard

- [ ] Solar/battery status display
- [ ] Power history charts
- [ ] Power ingest endpoint

### Phase 4 — All-Sky Viewer

- [ ] Latest all-sky image display
- [ ] Image archive browser (date/time navigation)
- [ ] Image metadata ingest endpoint
- [ ] Timelapse generation or linking

### Phase 5 — Observatory & Security

- [ ] Admin panel (site config, user role management)
- [ ] Alert log viewer
- [ ] Observatory status page (roof state, telescope state)
- [ ] Audit logging for admin actions

---

## Test Strategy

| Layer | Framework | Purpose |
|-------|-----------|---------|
| Unit | MSTest + Moq + FluentAssertions | Controller/service logic with mocked dependencies |
| API | MSTest + WebApplicationFactory | Integration tests against full ASP.NET Core pipeline |
| E2E | Playwright + MSTest | Browser-level tests against running server |

All tests must pass (`dotnet test`) before any PR is created. E2E tests are skipped by default (`[Ignore]`) unless the server is running.

---

## Constraints

- .NET SDK `10.0.203` (pinned in `global.json`)
- Zero build warnings, zero build errors — mandatory
- No `npm`, `npx`, Node.js, or Python
- `az` CLI is available for Azure/Entra resource management
- No heredoc syntax in shell scripts (use file-creation tools instead)
- Central package management via `Directory.Packages.props`
- Branch naming: `feature/<issue#>-<short-desc>`, `fix/<issue#>-<short-desc>`
- Commit style: Conventional commits (`feat:`, `fix:`, `chore:`, `refactor:`, `test:`, `docs:`)
- Merge strategy: Squash merge into `main`

---

## Legacy Schema Reference

The full legacy `dbo` schema (15 tables, 6 views, 40 stored procedures, 3 functions) is captured in `src/HVO.Database/` as a `Microsoft.Build.Sql` SQL project. This is a **read-only reference** — no new development targets the `dbo` schema.
