namespace HVO.Edge.Contracts.PowerSystem;

public sealed record PowerSystemSnapshot(
    DateTime ObservedAtUtc,
    PowerSystemAcSnapshot? Ac = null,
    PowerSystemPvSnapshot? Pv = null,
    PowerSystemBatterySnapshot? Battery = null,
    IReadOnlyList<PowerSystemBatteryBankSnapshot>? BatteryBanks = null,
    IReadOnlyList<string>? Notes = null);
