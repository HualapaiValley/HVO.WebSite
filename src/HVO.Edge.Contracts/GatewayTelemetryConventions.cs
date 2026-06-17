namespace HVO.Edge.Contracts;

public static class GatewayTelemetryConventions
{
    public static class ResourceAttributes
    {
        public const string ServiceName = "service.name";
        public const string GatewayId = "hvo.gateway.id";
        public const string GatewayType = "hvo.gateway.type";
        public const string SiteId = "hvo.site.id";
        public const string SourceId = "hvo.source.id";
        public const string DeviceId = "hvo.device.id";
    }

    public static class OperationNames
    {
        public const string DevicePoll = "gateway.device.poll";
        public const string DeviceConnect = "gateway.device.connect";
        public const string DeviceRead = "gateway.device.read";
        public const string OutboxEnqueue = "gateway.outbox.enqueue";
        public const string OutboxSweep = "gateway.outbox.sweep";
        public const string OutboxForward = "gateway.outbox.forward";
        public const string OutboxRetry = "gateway.outbox.retry";
        public const string OutboxDeadLetter = "gateway.outbox.dead_letter";
        public const string OutboxRequeue = "gateway.outbox.requeue";
        public const string HealthEvaluate = "gateway.health.evaluate";
    }

    public static class MetricNames
    {
        public const string OutboxDepth = "gateway.outbox.depth";
        public const string OutboxFailed = "gateway.outbox.failed";
        public const string OutboxForwardSuccess = "gateway.outbox.forward.success";
        public const string OutboxForwardFailure = "gateway.outbox.forward.failure";
        public const string DeviceFreshnessSeconds = "gateway.device.freshness.seconds";
        public const string DevicePollFailure = "gateway.device.poll.failure";
    }

    public static class Tags
    {
        public const string GatewayId = ResourceAttributes.GatewayId;
        public const string GatewayType = ResourceAttributes.GatewayType;
        public const string SourceId = ResourceAttributes.SourceId;
        public const string DeviceId = ResourceAttributes.DeviceId;
        public const string Operation = "hvo.operation";
        public const string FailureKind = "hvo.failure.kind";
        public const string PayloadType = "hvo.payload.type";
        public const string HealthState = "hvo.health.state";
    }
}
