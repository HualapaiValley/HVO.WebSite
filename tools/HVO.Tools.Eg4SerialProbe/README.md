# EG4 6500EX Serial Proof Tool

This disposable tool sends only the fixed PI30 inquiry commands represented by `Eg46500ExInquiry`. It cannot accept an arbitrary command and contains no setting, reset, firmware, or write operation.

Use only with a proven 6500EX RS232/COM cable on an unowned stable `/dev/serial/by-id/...` path. Do not use it on the BMS RS485 connector. Run the path-only identity form first; continue to the status form only when `QMN` is `MKS2-6500` and `QGMN` is `045`.

```bash
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter>
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter> status --confirm-rs232-com
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter> energy --confirm-rs232-com
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter> mppt --confirm-rs485
```

The status and energy forms repeat and validate identity in the same session. Energy sends only the fixed read-only `QET` and `QLT` lifetime counter inquiries and requires documented eight-digit Wh responses. The confirmation flag asserts that a human has traced the cable to the RS232/COM connector. The tool performs one bounded sequence and exits. Do not run it concurrently with WatchPower, SolarAssistant, or another inverter poller.

The MPPT form is separate from PI30. It permits only the production unit 1 read of holding registers 200-217 and requires a stable `/dev/serial/by-id/...` path connected to the MPPT100-48HV RS485 adapter.
