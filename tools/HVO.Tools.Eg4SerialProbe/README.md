# EG4 6500EX Serial Proof Tool

This disposable tool sends only the fixed PI30 inquiry commands represented by `Eg46500ExInquiry`. It cannot accept an arbitrary command and contains no setting, reset, firmware, or write operation.

Use only with a proven 6500EX RS232/COM cable on an unowned stable `/dev/serial/by-id/...` path. Do not use it on the BMS RS485 connector. Run the path-only identity form first; continue to the status form only when `QMN` is `MKS2-6500` and `QGMN` is `045`.

```bash
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter>
dotnet run --project tools/HVO.Tools.Eg4SerialProbe -- /dev/serial/by-id/<adapter> status --confirm-rs232-com
```

The status form repeats and validates identity in the same session before sending one `QPIGS` inquiry. The confirmation flag asserts that a human has traced the cable to the RS232/COM connector. The tool performs one bounded sequence and exits. Do not run it concurrently with WatchPower, SolarAssistant, or another inverter poller.
