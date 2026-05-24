# JK BMS Session Lifecycle

Status: active refactor target for Pi-hosted deployment on `devPi5`.

## Why This Exists

The current `HVO.Hardware.JkBms` runtime still centers reconnect behavior around persistent BLE scan loops. That was useful while proving out BlueZ connection constraints, but it is not the best long-term model for the validated Pi deployment.

The proven reference is `tools/HVO.Tools.JkBleConsole`, which successfully:

- resolved all configured JK devices on `devPi5`
- connected them sequentially
- kept successful sessions open
- polled across active sessions for repeated rounds
- worked both bare-host and in Docker on Pi

The JK service should move to that session model.

## Current Runtime Summary

Today the JK service works like this:

1. `BmsPollerWorker` creates one `JkBmsDevice` session per configured BMS.
2. Devices are grouped per adapter.
3. `BluetoothAdapterCoordinator` runs a long-lived scan loop per adapter.
4. When a wanted advertising device is seen, the scan loop stops scanning and hands a BlueZ `Device` object to `JkBmsDevice.SignalDeviceReadyAsync(...)`.
5. The session completes GATT setup, starts notifications, then enters its poll loop.
6. If transport/polling fails, the session resets and blocks until the scan loop rediscovers the device.

This design serializes connect attempts correctly, but it makes scanning a permanent control-plane dependency.

## Target Runtime Model

The target model is:

1. Resolve configured devices.
2. Connect devices sequentially per adapter.
3. Keep successful sessions open.
4. Poll across active sessions on normal intervals.
5. Reconnect only failed/disconnected sessions.
6. Use scanning as a recovery tool, not as the permanent runtime backbone.

Important constraint:

- Connect attempts remain sequential per adapter.
- Each connect attempt must use a short timeout so one bad device does not stall the rest.
- Healthy connected sessions must continue polling while reconnect work happens for unhealthy sessions.

## Session States

Each configured device should conceptually move through these states:

1. `Unresolved`
2. `Resolved`
3. `Connecting`
4. `ConnectedInitializing`
5. `ConnectedPolling`
6. `DisconnectedRetryPending`
7. `Disabled` or `PermanentFault` only if explicitly configured or administratively stopped

The runtime should not require all devices to be in the same state at once.

## Per-Adapter Orchestration

Per adapter:

1. Maintain a list of unresolved or disconnected sessions.
2. Attempt connects sequentially.
3. Use a short per-device connect/setup timeout.
4. After one quick pass, loop back later only for devices that are still not connected.

Desired behavior:

- device 1 failing must not block device 7 for a long timeout chain
- one full pass should be quick even when multiple devices are down
- connected devices keep polling during reconnect attempts for other devices

## Data Lanes

JK data is not just one constant cell-info poll loop. The runtime should treat these as separate lanes.

### 1. Hot lane: cell info

High-cadence repeating poll:

- explicit cell-info request
- update live state
- write reading to outbox
- evaluate alarms from each reading

This is the steady-state poll loop.

### 2. Warm lane: session initialization snapshots

Run when a device session first becomes healthy, and again after reconnect:

- fetch device info explicitly
- capture or refresh settings/config snapshot
- compare with cached or last-sent state
- include changed snapshots in the next durable outbox write

This lane exists so startup/reconnect can refresh low-frequency metadata without bloating every steady-state poll.

### 3. Cool lane: stale refresh

Optional low-frequency refresh for metadata:

- if no cached metadata exists
- if cached metadata is older than a threshold
- if reconnect occurred after full transport loss
- if an operator explicitly requests refresh later from the UI

These queries should never run every normal poll cycle.

## Device Info Handling

Desired rule:

- query device info explicitly on connect or reconnect
- cache the latest device info in session state
- include it in the outbox only when changed or stale

This is a clean explicit command path and should be part of the session initialization workflow.

## Settings / Configuration Handling

Current constraint:

- settings are currently captured from a spontaneous `0x01` frame observed during exchanges
- there is no proven explicit settings-query path in the current service code yet

Therefore the refactor should treat settings like this for now:

- on connect or reconnect, run the initialization sequence that gives the session the best chance to observe a valid settings frame
- if a valid settings snapshot is captured, cache it in session state
- if it is missing, keep the last known snapshot and mark the refresh as incomplete rather than blocking steady-state polling

Do not let the session design depend on solving the explicit settings-query question first.

## Alarm Handling

Alarm handling remains tied to the hot lane:

- alarms are derived from each `CellInfoPacket`
- no separate alarm query lane is needed
- alarm transitions should continue to be detected from normal readings

## Local Cache / Snapshot Policy

Desired rule:

- on connect or reconnect, refresh metadata when it is missing, stale, or the transport was fully rebuilt
- use local cached state to avoid redundant upstream writes
- use hash-based dedupe before marking a config or device-info snapshot as last-sent

Practical effect:

- fresh enough metadata on new sessions
- low BLE overhead in steady state
- no need to resend config or device info on every poll

## Recommended Refactor Sequence

1. Keep `JkBmsDevice` as the per-device session owner.
2. Move connection orchestration away from permanent scan loops.
3. Add a per-session initialization phase for device info and settings capture.
4. Keep the existing outbox hash-dedupe behavior.
5. Update runtime config away from the temporary `hci2` test defaults.
6. Apply the same conditional OTLP exporter registration fix used in Davis.

## Known Unknowns

The main protocol unknown still worth isolating is settings retrieval:

- explicit device-info query is proven
- spontaneous settings-frame capture is proven
- explicit settings-query behavior is not yet proven in this service

That should be treated as a contained follow-up, not a blocker for the main session-lifecycle refactor.
