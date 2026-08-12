namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public interface ISmartShuntSessionState
{
    SmartShuntLiveSample? CurrentSample { get; }
    bool IsConnected { get; }
    string? LastError { get; }
}
