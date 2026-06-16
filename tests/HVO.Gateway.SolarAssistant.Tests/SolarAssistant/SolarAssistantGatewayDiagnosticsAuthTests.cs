using FluentAssertions;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using Microsoft.AspNetCore.Http;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantGatewayDiagnosticsAuthTests
{
    [TestMethod]
    public void HasMatchingApiKey_MissingHeader_ReturnsFalse()
    {
        var httpContext = new DefaultHttpContext();

        SolarAssistantGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, "local-test-key")
            .Should().BeFalse();
    }

    [TestMethod]
    public void HasMatchingApiKey_MatchingHeader_ReturnsTrue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "local-test-key";

        SolarAssistantGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, "local-test-key")
            .Should().BeTrue();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("REPLACE_ME")]
    [DataRow("__SET_SOLAR_ASSISTANT_API_KEY__")]
    public void HasMatchingApiKey_UnusableConfiguredKey_ReturnsFalse(string configuredApiKey)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = configuredApiKey;

        SolarAssistantGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, configuredApiKey)
            .Should().BeFalse();
    }
}
