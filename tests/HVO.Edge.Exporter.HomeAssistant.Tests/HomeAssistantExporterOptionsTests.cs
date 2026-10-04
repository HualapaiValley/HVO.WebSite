using FluentAssertions;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
public sealed class HomeAssistantExporterOptionsTests
{
    [TestMethod]
    public void Defaults_ToDisabledSafeState()
    {
        new HomeAssistantExporterOptions().Enabled.Should().BeFalse();
        new HomeAssistantExporterOptions().Mappings.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(0, 300, 30, 30)]
    [DataRow(5001, 300, 30, 30)]
    [DataRow(250, 0, 30, 30)]
    [DataRow(250, 3601, 30, 30)]
    [DataRow(250, 300, -1, 30)]
    [DataRow(250, 300, 301, 30)]
    [DataRow(250, 300, 30, -1)]
    [DataRow(250, 300, 30, 301)]
    public void Validate_RejectsUnboundedObservationSettings(int window, int freshness, int skew, int future)
    {
        var options = TestOptions.Create();
        options.CoalescingWindowMilliseconds = window;
        options.RequiredFieldFreshnessSeconds = freshness;
        options.MaxFieldSkewSeconds = skew;
        options.MaxFutureClockSkewSeconds = future;
        new HomeAssistantExporterOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_AcceptsExplicitTplinkAndGoveeMappings()
    {
        var result = new HomeAssistantExporterOptionsValidator().Validate(null, TestOptions.Create());
        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_RejectsMqttPlatform()
    {
        var options = TestOptions.Create();
        options.Mappings[0].ExpectedPlatform = "mqtt";

        var result = new HomeAssistantExporterOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_RejectsHvoOwnedEntity()
    {
        var options = TestOptions.Create();
        options.Mappings[0].Entities[0].EntityId = "sensor.hvo_gateway_voltage";

        new HomeAssistantExporterOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_RejectsCredentialsInEndpoint()
    {
        var options = TestOptions.Create();
        options.Endpoint = "ws://token@example.test/api/websocket";

        new HomeAssistantExporterOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_RejectsInsecureCentralIngestByDefault()
    {
        var options = TestOptions.Create();
        options.CentralIngestEndpoint = "http://ingest.test/";

        new HomeAssistantExporterOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_RejectsUnapprovedContractPlatformAndOptionalMandatoryMetric()
    {
        var options = TestOptions.Create();
        options.Mappings[0].ExpectedPlatform = "template";
        options.Mappings[0].Entities[0].Required = false;

        new HomeAssistantExporterOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }
}
