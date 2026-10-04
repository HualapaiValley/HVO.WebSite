# HVO.Tools.SmartShuntBleConsole

This standalone Linux/BlueZ probe inspects public GATT and historical Victron private-stream behavior. [Program.cs](Program.cs) is the CLI/decoder source and [the project](HVO.Tools.SmartShuntBleConsole.csproj) depends on `Linux.Bluetooth`/D-Bus. It does not participate in the collector's adapter coordination, acquisition authority or outbox. The current [SmartShunt collector](../../src/HVO.Hardware.VictronSmartShunt/README.md) and [manual](../../docs/gateways/victron-smartshunt.md) own the production public-GATT contract; [#352](https://github.com/HualapaiValley/HVO.WebSite/issues/352) owns unresolved parity/private-field work.

## Build and validation

Use the exact [pinned SDK](../../global.json) from the repository root:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build tools/HVO.Tools.SmartShuntBleConsole/HVO.Tools.SmartShuntBleConsole.csproj --no-restore --nologo
```

No dedicated console test assembly exists. [SmartShunt non-live tests](../../tests/README.md#hvohardwarevictronsmartshunttests) qualify the production decoder/session with fakes; they do not qualify arbitrary probe writes or private profiles. No physical command is needed for a build/documentation check.

## Implemented modes

```text
HVO.Tools.SmartShuntBleConsole <services|read|notify|write|victron-init|private-monitor|public-snapshot|public-monitor> <BLE-MAC>
  [--adapter hci0] [--characteristic UUID] [--hex 0102]
  [--export-private-decoded path.jsonl] [--private-profile live|rich|reference|extended|syncprobe]
  [--count 1] [--interval-seconds 5] [--duration-seconds 30]
```

| Mode | Actual behavior/write boundary |
|---|---|
| `services` | Connects and enumerates service/characteristic UUIDs and flags. |
| `read` | Reads the selected characteristic; default count is one. |
| `notify` | Starts/stops notifications for the selected characteristic and logs raw values. |
| `write` | Sends arbitrary `--hex` bytes to the selected characteristic. No production allowlist or safe telemetry-only guarantee. |
| `victron-init` | Writes private initialization/profile packets and keepalives on `306b0002/3/4`. |
| `private-monitor` | Performs the same private writes, decodes received observations and can append decoded JSONL to `--export-private-decoded`. |
| `public-snapshot` | Writes the public keepalive, then reads the fixed public field set. |
| `public-monitor` | Writes public keepalives, reads initial values and subscribes to public fields for the selected duration. |

Defaults are adapter `hci0`, characteristic `97580002-ddf1-48be-b73e-182664615d8e` for generic modes, private profile `live`, interval five seconds and duration 30 seconds. Private init/monitor modes instead select the `306b` streams. Profiles `rich`, `reference`, `extended` and `syncprobe` send additional probe sequences; their names do not establish safe settings/SOC effects or production support. Public snapshot/monitor also perform BLE writes and must not be described as passive acquisition. Research decoder labels such as `Temperature?`/raw values are not a canonical persistence schema.

## Preconditions and evidence

Physical use requires separately authorized Linux BlueZ/system D-Bus access and exclusive ownership of the identified adapter/device. The collector, phone app and this probe must not compete for a connection. The console prints device addresses, raw bytes and interpreted observations; retain them only in approved evidence locations and protect any sensitive private fields. A successful connection or decoded output does not prove production parity, supported setting writes or a recoverable state transition.

[Historical SmartShunt research](../../docs/archive/2026-05-25-smartshunt-plan.md) preserves original field/probe assumptions and unresolved gates. Use the [current manual](../../docs/gateways/victron-smartshunt.md) and [gateway operations](../../docs/GATEWAY_OPERATIONS.md) for acquisition/configuration ownership. Do not copy a private probe command into routine collector validation or enable a second writer based on this tool's existence.
