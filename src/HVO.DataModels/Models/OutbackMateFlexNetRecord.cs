using System;
using System.Collections.Generic;

namespace HVO.DataModels.Models;

public partial class OutbackMateFlexNetRecord
{
    public int Id { get; set; }

    public DateTimeOffset RecordDateTime { get; set; }

    public byte HubPort { get; set; }

    public bool ShuntAenabled { get; set; }

    public decimal ShuntAamps { get; set; }

    public bool ShuntBenabled { get; set; }

    public decimal ShuntBamps { get; set; }

    public bool ShuntCenabled { get; set; }

    public decimal ShuntCamps { get; set; }

    public decimal BatteryVoltage { get; set; }

    public byte BatteryStateOfCharge { get; set; }

    public short? BatteryTemperatureC { get; set; }

    public byte? ExtraValueTypeId { get; set; }

    public decimal? ExtraValue { get; set; }

    public bool ChargeParamsMet { get; set; }

    public byte RelayState { get; set; }

    public byte RelayMode { get; set; }
}
