# HVO.Tools.Eg4SerialProbe

This disposable tool sends only the fixed PI30 inquiry commands represented by `Eg46500ExInquiry`. It cannot accept an arbitrary command and contains no setting, reset or firmware mutation. Transport writes send the fixed read inquiries, not device-control commands.

The [entry point](Program.cs) also supports the fixed MPPT100-48HV read and the production 6500EX HID transport. [Project dependencies](HVO.Tools.Eg4SerialProbe.csproj) are the [headless EG4 collector](../../src/HVO.Hardware.Eg4/README.md) and System.IO.Ports. [Protocol/proof guidance](../../docs/gateways/eg4/deployment-and-shadow-validation.md) owns approved hardware commissioning; this tool is not a second production poller.

## Local build and tests

Use the exact [pinned SDK](../../global.json) from the repository root:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build tools/HVO.Tools.Eg4SerialProbe/HVO.Tools.Eg4SerialProbe.csproj --no-restore --nologo
```

[EG4 non-live tests](../../tests/README.md#hvohardwareeg4tests) cover the shared allowlist/transports/parsers with fakes and preserved captures. No physical probe is required for build/issue validation. Physical port access needs applicable operational authorization and exclusive ownership.

## Physical proof boundary

For serial 6500EX proof, use a proven RS232/COM cable on an unowned stable `/dev/serial/by-id/...` path. Do not use it on the BMS RS485 connector. Run the path-only identity form first; continue to the status form only when `QMN` is `MKS2-6500` and `QGMN` is `045`.

The following shows argument shapes; `<adapter>` is a placeholder for the proven device-path basename, not a shell-ready command:

```text
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter>
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter> status --confirm-rs232-com
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter> energy --confirm-rs232-com
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter> mppt --confirm-rs485
```

The status and energy forms repeat and validate identity in the same session. Energy sends only the fixed read-only `QET` and `QLT` lifetime counter inquiries and requires documented eight-digit Wh responses. The confirmation flag asserts that a human has traced the cable to the RS232/COM connector. The tool performs one bounded sequence and exits. Do not run it concurrently with WatchPower, SolarAssistant, or another inverter poller.

The MPPT form is separate from PI30. It permits only the production unit 1 read of holding registers 200-217 and requires a stable `/dev/serial/by-id/...` path connected to the MPPT100-48HV RS485 adapter.

The existing HID branch accepts `/dev/hidrawN` or `/dev/hvo/eg4-6500ex-...` only with `status|energy --confirm-rs232-com`; it validates identity in the same session. The path-only identity form is serial-only. HID/serial access permissions and proven device-path ownership are prerequisites, not supplied by the confirmation flag. Preserve the [6500EX protocol](../../docs/gateways/eg4/6500ex-protocol.md) and [MPPT protocol/proof notes](../../docs/gateways/eg4/mppt100-48hv-protocol.md) when interpreting results.
