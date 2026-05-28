namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public interface ISmartShuntPrivateInfoSource
{
    Task<SmartShuntDeviceInfo?> TryReadAsync(CancellationToken ct);
}
