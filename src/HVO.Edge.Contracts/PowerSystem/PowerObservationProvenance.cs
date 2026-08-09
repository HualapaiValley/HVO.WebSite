using System.Text.Json.Serialization;

namespace HVO.Edge.Contracts.PowerSystem;

[JsonConverter(typeof(JsonStringEnumConverter<PowerObservationProvenance>))]
public enum PowerObservationProvenance
{
    Unknown = 0,
    Direct = 1,
    SourceAggregate = 2,
    Derived = 3,
}
