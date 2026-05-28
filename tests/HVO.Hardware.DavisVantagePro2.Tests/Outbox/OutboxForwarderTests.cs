using System.Net;
using System.Reflection;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Workers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public class OutboxForwarderTests
{
    [TestMethod]
    public async Task ForwardBatchAsync_InvalidPayload_DeadLettersWithoutHttpCall()
    {
        int calls = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Created);
        }));
        using var forwarder = CreateForwarder(client);
        var record = CreateRecord("not json");

        await InvokeForwardBatchAsync(forwarder, client, [record]);

        calls.Should().Be(0);
        record.Status.Should().Be(OutboxStatus.Failed);
        record.FailureKind.Should().Be(OutboxFailureKind.InvalidPayload);
        record.LastError.Should().Contain("Invalid outbox payload JSON");
    }

    [TestMethod]
    public async Task ForwardBatchAsync_ApiValidationFailure_RecordsDeadLetterReason()
    {
        var recordedAt = new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc);
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent($"{{\"failed\":[{{\"recordedAt\":\"{recordedAt:O}\",\"error\":\"bad format\"}}]}}")
            }));
        using var forwarder = CreateForwarder(client);
        var record = CreateRecord("{}", recordedAt);

        await InvokeForwardBatchAsync(forwarder, client, [record]);

        record.Status.Should().Be(OutboxStatus.Failed);
        record.FailureKind.Should().Be(OutboxFailureKind.ApiValidation);
        record.LastError.Should().Be("bad format");
    }

    [TestMethod]
    public async Task ForwardBatchAsync_TransientFailureAfterMaxAttempts_RecordsRetryExhaustedReason()
    {
        using var client = new HttpClient(new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("cloud server down")
                }));
        using var forwarder = CreateForwarder(
            client,
            new OutboxOptions { ApiEndpoint = "https://example.test/api/v1/weather/raw", ApiKey = "test", MaxRetryAttempts = 1 });
        var record = CreateRecord("{}");

        await InvokeForwardBatchAsync(forwarder, client, [record]);

        record.Status.Should().Be(OutboxStatus.Failed);
        record.FailureKind.Should().Be(OutboxFailureKind.TransientExhausted);
        record.LastError.Should().Contain("Giving up after 1 attempts");
        record.LastError.Should().Contain("HTTP 503");
    }

    private static OutboxRecord CreateRecord(string payload, DateTime? recordedAt = null) => new()
    {
        Id = 42,
        RecordedAtUtc = recordedAt ?? new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc),
        Payload = payload,
        Status = OutboxStatus.Pending,
    };

    private static async Task InvokeForwardBatchAsync(OutboxForwarder forwarder, HttpClient client, List<OutboxRecord> records)
    {
        var method = typeof(OutboxForwarder).GetMethod("ForwardBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ForwardBatchAsync was not found.");
        var task = (Task?)method.Invoke(forwarder, [client, records, CancellationToken.None])
            ?? throw new InvalidOperationException("ForwardBatchAsync did not return a task.");
        await task;
    }

    private static OutboxForwarder CreateForwarder(HttpClient client, OutboxOptions? options = null) => new(
        scopeFactory: null!,
        httpFactory: new StubHttpClientFactory(client),
        options: Options.Create(options ?? new OutboxOptions { ApiEndpoint = "https://example.test/api/v1/weather/raw", ApiKey = "test" }),
        telemetry: new DavisTelemetry(),
        telemetryService: CreateTelemetryServiceProxy(),
        logger: NullLogger<OutboxForwarder>.Instance);

    private static ITelemetryService CreateTelemetryServiceProxy()
    {
        var startOperationMethod = typeof(ITelemetryService).GetMethod("StartOperation")
            ?? throw new InvalidOperationException("ITelemetryService.StartOperation not found.");
        var operationType = startOperationMethod.ReturnType;

        var createGeneric = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

        var createOperationProxy = createGeneric.MakeGenericMethod(operationType, typeof(NoOpDispatchProxy));
        var operationProxy = createOperationProxy.Invoke(null, null)!;
        ((NoOpDispatchProxy)operationProxy).ReturnSelf = operationProxy;

        var createServiceProxy = createGeneric.MakeGenericMethod(typeof(ITelemetryService), typeof(NoOpDispatchProxy));
        var serviceProxy = createServiceProxy.Invoke(null, null)!;
        ((NoOpDispatchProxy)serviceProxy).StartOperationResult = operationProxy;
        return (ITelemetryService)serviceProxy;
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private class NoOpDispatchProxy : DispatchProxy
    {
        public object? ReturnSelf { get; set; }
        public object? StartOperationResult { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                return null;

            if (targetMethod.Name == "StartOperation")
                return StartOperationResult;

            if (targetMethod.ReturnType == typeof(void))
                return null;

            if (targetMethod.ReturnType.IsValueType)
                return Activator.CreateInstance(targetMethod.ReturnType);

            if (ReturnSelf is not null && targetMethod.ReturnType.IsInstanceOfType(ReturnSelf))
                return ReturnSelf;

            return null;
        }
    }
}
