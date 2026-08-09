namespace HVO.Edge.Contracts;

public static class GatewayTelemetryConventions
{
    public const string MeterName = "HVO.Edge";
    public const string ActivitySourceName = "HVO.Edge";
    public const string Version = "1.0.0";

    public static class ResourceAttributes
    {
        public const string ServiceName = "service.name";
        public const string GatewayId = "hvo.gateway.id";
        public const string GatewayType = "hvo.gateway.type";
        public const string SiteId = "hvo.site.id";
        public const string SourceId = "hvo.source.id";
        public const string DeviceId = "hvo.device.id";
        public const string ServiceVersion = "service.version";
        public const string ServiceInstanceId = "service.instance.id";
        public const string DeploymentEnvironment = "deployment.environment.name";
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
        public const string DeviceConnectAttempt = "gateway.device.connect.attempt";
        public const string DeviceConnectFailure = "gateway.device.connect.failure";
        public const string DeviceReconnect = "gateway.device.reconnect";
        public const string DeviceConnectDuration = "gateway.device.connect.duration";
        public const string DeviceReadAttempt = "gateway.device.read.attempt";
        public const string DeviceReadFailure = "gateway.device.read.failure";
        public const string DeviceReadDuration = "gateway.device.read.duration";
        public const string DevicePollAttempt = "gateway.device.poll.attempt";
        public const string OutboxDepth = "gateway.outbox.depth";
        public const string OutboxFailed = "gateway.outbox.failed";
        public const string OutboxForwardSuccess = "gateway.outbox.forward.success";
        public const string OutboxForwardFailure = "gateway.outbox.forward.failure";
        public const string DeviceFreshnessSeconds = "gateway.device.freshness.seconds";
        public const string DevicePollFailure = "gateway.device.poll.failure";
        public const string DevicePollDuration = "gateway.device.poll.duration";
        public const string OutboxForwardDuration = "gateway.outbox.forward.duration";
        public const string HealthEvaluation = "gateway.health.evaluation";
        public const string HealthEvaluationDuration = "gateway.health.evaluation.duration";

        public static readonly IReadOnlyList<string> All =
        [
            DeviceConnectAttempt, DeviceConnectFailure, DeviceReconnect, DeviceConnectDuration,
            DeviceReadAttempt, DeviceReadFailure, DeviceReadDuration,
            DevicePollAttempt, DevicePollFailure, DevicePollDuration, DeviceFreshnessSeconds,
            OutboxDepth, OutboxFailed, OutboxForwardSuccess, OutboxForwardFailure, OutboxForwardDuration,
            HealthEvaluation, HealthEvaluationDuration
        ];
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
        public const string DeviceType = "hvo.device.type";
        public const string Result = "hvo.result";
    }

    public static class Results
    {
        public const string Success = "success";
        public const string Failure = "failure";
        public const string Degraded = "degraded";
        public const string Skipped = "skipped";
        public const string Unknown = "unknown";
    }

    public static class Units
    {
        public const string Attempt = "{attempt}";
        public const string Failure = "{failure}";
        public const string Reconnect = "{reconnect}";
        public const string Record = "{record}";
        public const string Evaluation = "{evaluation}";
        public const string Seconds = "s";
    }
}
