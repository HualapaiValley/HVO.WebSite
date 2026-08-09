using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class PowerBatteryObservationTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void DerivedObservation_RoundTripsIdentitySignsEnumsAndInputs()
    {
        var observedAtUtc = new DateTime(2026, 8, 9, 5, 30, 0, DateTimeKind.Utc);
        var observation = new PowerBatteryObservation(
            SourceId: "derived-inverter-total",
            DeviceId: "all-inverters",
            Source: PowerMetricSource.Derived,
            Role: PowerMeasurementRole.DerivedAggregate,
            MeasurementPoint: "inverter-branches-total",
            ObservedAtUtc: observedAtUtc,
            VoltageV: null,
            CurrentA: 20.5,
            PowerW: 1_100,
            StateOfChargePercent: null,
            Provenance: PowerObservationProvenance.Derived,
            Confidence: "timestamp-aligned",
            Inputs:
            [
                new PowerObservationInput("smartshunt-main", observedAtUtc.AddSeconds(-1), "smartshunt-lifepo4"),
                new PowerObservationInput("eg4-mppt-east", observedAtUtc.AddSeconds(-2), "mppt-east"),
            ]);

        var json = JsonSerializer.Serialize(observation, WebJsonOptions);
        var result = JsonSerializer.Deserialize<PowerBatteryObservation>(json, WebJsonOptions);

        json.Should().Contain("\"source\":\"Derived\"");
        json.Should().Contain("\"role\":\"DerivedAggregate\"");
        json.Should().Contain("\"provenance\":\"Derived\"");
        result.Should().NotBeNull();
        result!.SourceId.Should().Be("derived-inverter-total");
        result.DeviceId.Should().Be("all-inverters");
        result.ObservedAtUtc.Should().Be(observedAtUtc);
        result.ObservedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        result.CurrentA.Should().Be(20.5);
        result.PowerW.Should().Be(1_100);
        result.VoltageV.Should().BeNull();
        result.Inputs.Should().HaveCount(2);
        result.Inputs![1].SourceId.Should().Be("eg4-mppt-east");
        result.Inputs[1].ObservedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [TestMethod]
    public void CanonicalSigns_ArePreservedWithoutImplicitTransformation()
    {
        var discharge = CreateObservation(PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, 18.5, 980);
        var charge = CreateObservation(PowerMetricSource.Eg4Mppt10048Hv, PowerMeasurementRole.ChargeControllerBranch, -12.25, -650);

        var dischargeResult = RoundTrip(discharge);
        var chargeResult = RoundTrip(charge);

        dischargeResult.CurrentA.Should().BePositive();
        dischargeResult.PowerW.Should().BePositive();
        chargeResult.CurrentA.Should().BeNegative();
        chargeResult.PowerW.Should().BeNegative();
    }

    [TestMethod]
    public void ContractEnums_SerializeAsStringsAndAcceptNumericValues()
    {
        foreach (var role in Enum.GetValues<PowerMeasurementRole>())
        {
            var observation = CreateObservation(PowerMetricSource.Eg46500Ex, role, null, null);
            JsonSerializer.Serialize(observation, WebJsonOptions).Should().Contain($"\"role\":\"{role}\"");
        }

        foreach (var source in Enum.GetValues<PowerMetricSource>())
        {
            var observation = CreateObservation(source, PowerMeasurementRole.BatteryPack, null, null);
            JsonSerializer.Serialize(observation, WebJsonOptions).Should().Contain($"\"source\":\"{source}\"");
        }

        foreach (var provenance in Enum.GetValues<PowerObservationProvenance>())
        {
            var observation = CreateObservation(
                PowerMetricSource.Eg46500Ex,
                PowerMeasurementRole.InverterBranch,
                null,
                null,
                provenance);
            JsonSerializer.Serialize(observation, WebJsonOptions).Should().Contain($"\"provenance\":\"{provenance}\"");
        }

        const string numericJson = """
            {"sourceId":"eg4-inverter-a","deviceId":"inverter-a","source":5,"role":3,"measurementPoint":"inverter-a-battery","observedAtUtc":"2026-08-09T05:30:00Z","provenance":1}
            """;
        var numericResult = JsonSerializer.Deserialize<PowerBatteryObservation>(numericJson, WebJsonOptions);

        numericResult.Should().NotBeNull();
        numericResult!.Source.Should().Be(PowerMetricSource.Eg46500Ex);
        numericResult.Role.Should().Be(PowerMeasurementRole.InverterBranch);
        numericResult.Provenance.Should().Be(PowerObservationProvenance.Direct);
    }

    [TestMethod]
    public void Constructor_RejectsInvalidIdentityTimestampEnumsAndDirection()
    {
        var utc = new DateTime(2026, 8, 9, 5, 30, 0, DateTimeKind.Utc);

        Action missingSource = () => new PowerBatteryObservation(
            " ", "device", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, "point", utc);
        Action missingDevice = () => new PowerBatteryObservation(
            "source", "", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, "point", utc);
        Action missingPoint = () => new PowerBatteryObservation(
            "source", "device", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, " ", utc);
        Action nonUtc = () => new PowerBatteryObservation(
            "source", "device", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, "point",
            DateTime.SpecifyKind(utc, DateTimeKind.Unspecified));
        Action undefinedRole = () => new PowerBatteryObservation(
            "source", "device", PowerMetricSource.Eg46500Ex, (PowerMeasurementRole)999, "point", utc);
        Action contradictoryDirection = () => new PowerBatteryObservation(
            "source", "device", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, "point", utc,
            CurrentA: -10, PowerW: 500);

        missingSource.Should().Throw<ArgumentException>();
        missingDevice.Should().Throw<ArgumentException>();
        missingPoint.Should().Throw<ArgumentException>();
        nonUtc.Should().Throw<ArgumentException>();
        undefinedRole.Should().Throw<ArgumentOutOfRangeException>();
        contradictoryDirection.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Deserialization_RejectsNonUtcTimestampAndUndefinedEnum()
    {
        const string nonUtcJson = """
            {"sourceId":"source","deviceId":"device","source":"Eg46500Ex","role":"InverterBranch","measurementPoint":"point","observedAtUtc":"2026-08-09T05:30:00"}
            """;
        const string undefinedRoleJson = """
            {"sourceId":"source","deviceId":"device","source":"Eg46500Ex","role":999,"measurementPoint":"point","observedAtUtc":"2026-08-09T05:30:00Z"}
            """;

        Action deserializeNonUtc = () => JsonSerializer.Deserialize<PowerBatteryObservation>(nonUtcJson, WebJsonOptions);
        Action deserializeUndefinedRole = () => JsonSerializer.Deserialize<PowerBatteryObservation>(undefinedRoleJson, WebJsonOptions);

        deserializeNonUtc.Should().Throw<ArgumentException>();
        deserializeUndefinedRole.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static PowerBatteryObservation CreateObservation(
        PowerMetricSource source,
        PowerMeasurementRole role,
        double? currentA,
        double? powerW,
        PowerObservationProvenance provenance = PowerObservationProvenance.Direct) => new(
            SourceId: "stable-source",
            DeviceId: "stable-device",
            Source: source,
            Role: role,
            MeasurementPoint: "battery-terminal",
            ObservedAtUtc: new DateTime(2026, 8, 9, 5, 30, 0, DateTimeKind.Utc),
            CurrentA: currentA,
            PowerW: powerW,
            Provenance: provenance);

    private static PowerBatteryObservation RoundTrip(PowerBatteryObservation observation) =>
        JsonSerializer.Deserialize<PowerBatteryObservation>(
            JsonSerializer.Serialize(observation, WebJsonOptions),
            WebJsonOptions)!;
}
