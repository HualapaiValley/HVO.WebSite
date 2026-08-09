using HVO.Hardware.Eg4.Protocol;

namespace HVO.Hardware.Eg4.Tests.Fixtures;

internal sealed class Eg4RegisterFixtureBuilder(ushort startAddress)
{
    private readonly SortedDictionary<ushort, ushort> _values = [];

    public Eg4RegisterFixtureBuilder Value(ushort offset, ushort value)
    {
        _values[offset] = value;
        return this;
    }

    public (Eg4ReadRegistersRequest Request, Eg4ReadRegistersResponse Response) Build(byte unitId, Eg4RegisterTable table)
    {
        var count = checked((ushort)(_values.Keys.DefaultIfEmpty((ushort)0).Max() + 1));
        var registers = new ushort[count];
        foreach (var (offset, value) in _values) registers[offset] = value;
        return (new Eg4ReadRegistersRequest(unitId, table, startAddress, count), new Eg4ReadRegistersResponse(registers));
    }
}
