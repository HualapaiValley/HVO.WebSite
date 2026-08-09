using System.Diagnostics;
using FluentAssertions;
using HVO.Edge.Contracts;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class W3CTraceContextTests
{
    [TestMethod]
    public void AddTraceContext_PropagatesCurrentCanonicalActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GatewayTelemetryConventions.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = HvoActivitySource.StartOperation(
            GatewayTelemetryConventions.OperationNames.OutboxForward,
            ActivityKind.Client,
            "gateway-1",
            "battery");
        activity.Should().NotBeNull();
        activity!.TraceStateString = "vendor=value";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test");

        request.AddTraceContext();

        request.Headers.GetValues(CloudEventsConstants.TraceParentHeader).Should().ContainSingle().Which.Should().Be(activity.Id);
        request.Headers.GetValues(CloudEventsConstants.TraceStateHeader).Should().ContainSingle().Which.Should().Be("vendor=value");
    }

    [TestMethod]
    public void AddTraceContext_WithoutActivity_DoesNotAddHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test");

        request.AddTraceContext();

        request.Headers.Contains(CloudEventsConstants.TraceParentHeader).Should().BeFalse();
    }
}
