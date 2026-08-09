namespace HVO.Hardware.Eg4.Protocol;

public enum Eg4RegisterTable
{
    Holding = 1,
    Input = 2,
}

public sealed record Eg4ReadRegistersRequest
{
    public Eg4ReadRegistersRequest(byte UnitId, Eg4RegisterTable Table, ushort StartAddress, ushort RegisterCount)
    {
        if (UnitId is < 1 or > 247) throw new ArgumentOutOfRangeException(nameof(UnitId));
        if (!Enum.IsDefined(Table)) throw new ArgumentOutOfRangeException(nameof(Table));
        if (RegisterCount is < 1 or > 125) throw new ArgumentOutOfRangeException(nameof(RegisterCount));
        if ((int)StartAddress + RegisterCount > ushort.MaxValue + 1) throw new ArgumentOutOfRangeException(nameof(RegisterCount));
        this.UnitId = UnitId;
        this.Table = Table;
        this.StartAddress = StartAddress;
        this.RegisterCount = RegisterCount;
    }

    public byte UnitId { get; }
    public Eg4RegisterTable Table { get; }
    public ushort StartAddress { get; }
    public ushort RegisterCount { get; }
}

public sealed record Eg4ReadRegistersResponse
{
    public Eg4ReadRegistersResponse(IEnumerable<ushort> Registers)
    {
        ArgumentNullException.ThrowIfNull(Registers);
        this.Registers = Array.AsReadOnly(Registers.ToArray());
    }
    public IReadOnlyList<ushort> Registers { get; }
}

public enum Eg4TransportFailureKind { Crc, MalformedFrame, Disconnected, Timeout, Protocol }

public sealed class Eg4TransportException : Exception
{
    public Eg4TransportException(Eg4TransportFailureKind kind, string message) : base(message) => Kind = kind;
    public Eg4TransportException(Eg4TransportFailureKind kind, string message, Exception innerException)
        : base(message, innerException) => Kind = kind;
    public Eg4TransportFailureKind Kind { get; }
}
