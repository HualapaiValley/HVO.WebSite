using System;
using System.Collections.Generic;

namespace HVO.DataModels.Models;

public partial class WebPowerSwitchConfiguration
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string SerialNumber { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string Username { get; set; } = null!;

    public string Password { get; set; } = null!;

    public bool Enabled { get; set; }
}
