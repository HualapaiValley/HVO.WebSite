using System.Text.Json.Serialization;

namespace HVO.Edge.Contracts.PowerSystem;

[JsonConverter(typeof(JsonStringEnumConverter<PowerMeasurementRole>))]
public enum PowerMeasurementRole
{
    Unknown = 0,
    BusNet = 1,
    BatteryPack = 2,
    InverterBranch = 3,
    ChargeControllerBranch = 4,
    AggregateEstimate = 5,
    DerivedAggregate = 6,
}
