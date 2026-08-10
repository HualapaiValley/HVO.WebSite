using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class PowerMpptDetailPayloadTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void Payload_RoundTripsIndependentTrackersAndDiagnosticOnlyControllerSoc()
    {
        var payload = new PowerMpptDetailPayload
        {
            SourceId = "eg4-mppt100-48hv-a",
            SourceSystem = "eg4-mppt100-48hv",
            DeviceId = "controller-a",
            RecordedAtUtc = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc),
            Trackers =
            [
                new PowerMpptTrackerDetail
                {
                    TrackerId = "mppt-1",
                    Name = "External array",
                    VoltageV = 400.6,
                    CurrentA = 1.2,
                    PowerW = 480,
                    Provenance = PowerObservationProvenance.Direct,
                },
            ],
            BatteryOutput = new PowerMpptBatteryOutputDetail
            {
                VoltageV = 54.3,
                CurrentA = -9.1,
                PowerW = -494.13,
                Provenance = PowerObservationProvenance.Derived,
            },
            Diagnostics =
            [
                new PowerMpptDiagnosticDetail
                {
                    Key = "controllerEstimatedSocPercent",
                    Name = "Controller estimated SOC percent",
                    Value = "90",
                },
            ],
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var result = JsonSerializer.Deserialize<PowerMpptDetailPayload>(json, JsonOptions);

        result.Should().BeEquivalentTo(payload);
        json.Should().Contain("\"provenance\":\"Direct\"");
        json.Should().Contain("\"currentA\":-9.1");
    }

    [TestMethod]
    public void PayloadType_MapsLegacyIdentifierToVersionedCloudEventType()
    {
        EdgePayloadTypes.ToCloudEventType(EdgePayloadTypes.Legacy.PowerMpptDetail)
            .Should().Be(EdgePayloadTypes.PowerMpptDetail);
    }
}
