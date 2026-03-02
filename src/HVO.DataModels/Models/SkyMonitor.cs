using System;
using System.Collections.Generic;

namespace HVO.DataModels.Models;

public partial class SkyMonitor
{
    public int Id { get; set; }

    public DateTimeOffset RecordDateTime { get; set; }

    public Guid DeviceId { get; set; }

    public decimal Ir { get; set; }

    public decimal Visible { get; set; }

    public decimal Lux { get; set; }

    public string Gain { get; set; } = null!;

    public decimal AmbientTemperature { get; set; }

    public decimal SkyTemperature { get; set; }
}
