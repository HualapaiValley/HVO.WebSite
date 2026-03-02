using System;
using System.Collections.Generic;

namespace HVO.DataModels.Models;

public partial class OutbackMateChargeControllerRecord
{
    public int Id { get; set; }

    public DateTimeOffset RecordDateTime { get; set; }

    public byte HubPort { get; set; }

    public byte PvAmps { get; set; }

    public byte PvVoltage { get; set; }

    public decimal ChargerAmps { get; set; }

    public decimal ChargerVoltage { get; set; }

    public short DailyAmpHoursProduced { get; set; }

    public short DailyWattHoursProduced { get; set; }

    public byte ChargerMode { get; set; }

    public byte ChargerAuxRelayMode { get; set; }

    public byte ChargerErrorMode { get; set; }
}
