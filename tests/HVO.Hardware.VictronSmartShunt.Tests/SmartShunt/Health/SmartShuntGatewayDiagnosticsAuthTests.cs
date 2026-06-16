using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using Microsoft.AspNetCore.Http;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt.Health;

[TestClass]
public sealed class SmartShuntGatewayDiagnosticsAuthTests
{
    [TestMethod]
    public void HasMatchingApiKey_MissingHeader_ReturnsFalse()
    {
        var httpContext = new DefaultHttpContext();

        SmartShuntGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, "local-test-key")
            .Should().BeFalse();
    }

    [TestMethod]
    public void HasMatchingApiKey_MatchingHeader_ReturnsTrue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "local-test-key";

        SmartShuntGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, "local-test-key")
            .Should().BeTrue();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("REPLACE_ME")]
    [DataRow("__SET_SMARTSHUNT_API_KEY__")]
    public void HasMatchingApiKey_UnusableConfiguredKey_ReturnsFalse(string configuredApiKey)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = configuredApiKey;

        SmartShuntGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, configuredApiKey)
            .Should().BeFalse();
    }
}
