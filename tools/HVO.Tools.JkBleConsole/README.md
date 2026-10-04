# HVO.Tools.JkBleConsole

This standalone Linux/BlueZ console probes JK BLE cell-frame acquisition for commissioning/research. It depends on `Linux.Bluetooth`/D-Bus, not the collector runtime, durable outbox or HA projection. [Program.cs](Program.cs) owns its CLI and fixed command. The deployed [JK collector](../../src/HVO.Hardware.JkBms/README.md) and [current manual](../../docs/gateways/jk-bms.md) own production sessions and capabilities.

## Build and ownership

From the repository root, use the exact [pinned SDK](../../global.json):

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build tools/HVO.Tools.JkBleConsole/HVO.Tools.JkBleConsole.csproj --no-restore --nologo
```

There is no dedicated console test assembly. [JK non-live tests](../../tests/README.md#hvohardwarejkbmstests) cover the production protocol/session, not this console's physical behavior. Building does not require BlueZ hardware or authorize connecting to a BMS.

## CLI and operational boundary

The implemented argument shape is:

```text
HVO.Tools.JkBleConsole <BLE-MAC> [<BLE-MAC> ...] [--adapter hci0] [--rounds 3] [--interval-seconds 5]
```

Defaults are adapter `hci0`, three rounds and five seconds between rounds. An authorized invocation requires Linux BlueZ/system D-Bus access, an identified adapter/device and sole ownership of the BLE session. Stop/exclude the existing collector and competing phone/probe sessions under an approved operational procedure; this tool does not use the collector's adapter coordinator.

The probe connects to service `FFE0`, subscribes to characteristic `FFE1`, and **writes** the fixed cell-info request before collecting a 300-byte response. It is not passive listening. Scan/connect/notification waits are bounded; output includes raw notification hex and per-round outcomes. It does not implement settings/password control or prove settings-query support. Its exit code reflects whether all requested sessions initially connected, so inspect `poll_failed` output rather than treating exit zero as proof every poll succeeded.

Do not run device commands as routine issue validation. Current operational ownership/endurance is documented in [JK endurance](../../docs/gateways/jkbms/deployment-and-endurance.md); the [session-design archive](../../docs/archive/jkbms-session-lifecycle.md) preserves earlier research without redefining the implemented collector. No runtime configuration, secret or device state is changed by this README.
