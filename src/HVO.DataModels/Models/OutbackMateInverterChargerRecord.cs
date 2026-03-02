using System;
using System.Collections.Generic;

namespace HVO.DataModels.Models;

public partial class OutbackMateInverterChargerRecord
{
    public int Id { get; set; }

    public DateTimeOffset RecordDateTime { get; set; }

    public byte HubPort { get; set; }

    public byte InverterCurrent { get; set; }

    public byte ChargerCurrent { get; set; }

    public byte BuyCurrent { get; set; }

    public short AcInputVoltage { get; set; }

    public short AcOutputVoltage { get; set; }

    public byte SellCurrent { get; set; }

    public int OperationalMode { get; set; }

    public int ErrorMode { get; set; }

    public int AcInputMode { get; set; }

    public decimal BatteryVoltage { get; set; }

    public int Misc { get; set; }

    public int WarningMode { get; set; }
}
