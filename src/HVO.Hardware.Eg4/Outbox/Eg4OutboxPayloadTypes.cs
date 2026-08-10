using HVO.Edge.Contracts;

namespace HVO.Hardware.Eg4.Outbox;

public static class Eg4OutboxPayloadTypes
{
    public const string Reading = EdgePayloadTypes.Legacy.PowerReading;
    public const string ReadingVersion = "1";
    public const string MpptDetail = EdgePayloadTypes.Legacy.PowerMpptDetail;
    public const string MpptDetailVersion = "1";
    public const string InverterDetail = EdgePayloadTypes.Legacy.PowerInverterDetail;
    public const string InverterDetailVersion = "1";
}
