using FluentAssertions;
using HVO.Edge.Contracts;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class GatewayApiKeyMatcherTests
{
    [TestMethod]
    public void IsMatch_MissingProvidedKey_ReturnsFalse()
    {
        GatewayApiKeyMatcher.IsMatch("local-test-key", null).Should().BeFalse();
        GatewayApiKeyMatcher.IsMatch("local-test-key", string.Empty).Should().BeFalse();
    }

    [TestMethod]
    public void IsMatch_MatchingProvidedKey_ReturnsTrue()
    {
        GatewayApiKeyMatcher.IsMatch("local-test-key", "local-test-key").Should().BeTrue();
    }

    [TestMethod]
    public void IsMatch_DifferentProvidedKey_ReturnsFalse()
    {
        GatewayApiKeyMatcher.IsMatch("local-test-key", "other-key").Should().BeFalse();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("REPLACE_ME")]
    [DataRow("__SET_GATEWAY_API_KEY__")]
    public void IsMatch_UnusableConfiguredKey_ReturnsFalse(string? configuredApiKey)
    {
        GatewayApiKeyMatcher.IsMatch(configuredApiKey, configuredApiKey).Should().BeFalse();
    }
}
