using FluentAssertions;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
public sealed class HomeAssistantExporterOptionsTests
{
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
