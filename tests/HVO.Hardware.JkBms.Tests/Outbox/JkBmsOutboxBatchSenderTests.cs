using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Outbox;

[TestClass]
public sealed class JkBmsOutboxBatchSenderTests
{
    private static readonly DateTime RecordedAt = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task SendAsync_UsesOneBoundedCloudEventsBatchAndStrictlyAccountsForResults()
    {
        var handler = new StubHandler(_ => Created("{\"inserted\":1,\"skipped\":1,\"failed\":[]}"));
        var outcomes = await Sender(handler).SendAsync([
            Record(1, "AA:BB:CC:DD:EE:01", RecordedAt),
            Record(2, "AA:BB:CC:DD:EE:02", RecordedAt.AddSeconds(1))
        ], CancellationToken.None);

        outcomes.Should().OnlyContain(outcome => outcome.Status == EdgeOutboxSendStatus.Sent);
        handler.RequestCount.Should().Be(1);
        handler.ApiKey.Should().Be("central-key");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetArrayLength().Should().Be(2);
        body.RootElement[0].GetProperty("type").GetString().Should().Be(EdgePayloadTypes.BmsReading);
    }

    [TestMethod]
    public async Task SendAsync_RetriesIncompleteSuccessAndPermanentlyRejectsMalformedPayload()
    {
        var incomplete = await Sender(new StubHandler(_ => Created("{}")))
            .SendAsync([Record(1, "AA:BB:CC:DD:EE:01", RecordedAt)], CancellationToken.None);
        incomplete.Single().Status.Should().Be(EdgeOutboxSendStatus.TransientFailure);

        var malformed = Record(2, "AA:BB:CC:DD:EE:02", RecordedAt);
        malformed.PayloadJson = "{";
        var invalid = await Sender(new StubHandler(_ => Created("{\"inserted\":0,\"skipped\":0,\"failed\":[]}")))
            .SendAsync([malformed], CancellationToken.None);
        invalid.Single().Status.Should().Be(EdgeOutboxSendStatus.PermanentFailure);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, EdgeOutboxSendStatus.PermanentFailure)]
    [DataRow(HttpStatusCode.Unauthorized, EdgeOutboxSendStatus.PermanentFailure)]
    [DataRow(HttpStatusCode.TooManyRequests, EdgeOutboxSendStatus.TransientFailure)]
    [DataRow(HttpStatusCode.ServiceUnavailable, EdgeOutboxSendStatus.TransientFailure)]
    public async Task SendAsync_ClassifiesCentralFailures(HttpStatusCode code, EdgeOutboxSendStatus expected)
    {
        var outcomes = await Sender(new StubHandler(_ => new HttpResponseMessage(code)))
            .SendAsync([Record(1, "AA:BB:CC:DD:EE:01", RecordedAt)], CancellationToken.None);
        outcomes.Single().Status.Should().Be(expected);
        outcomes.Single().Error.Should().Be($"Central ingest returned HTTP {(int)code}");
    }

    [TestMethod]
    public async Task SendAsync_PermanentRecordFailure_RetainsActionableRejectionReason()
    {
        var response = $$"""
            {"inserted":0,"skipped":0,"failed":[{"deviceAddress":"AA:BB:CC:DD:EE:01","recordedAtUtc":"{{RecordedAt:O}}","error":"Pack voltage must be positive"}]}
            """;

        var outcome = (await Sender(new StubHandler(_ => Created(response)))
            .SendAsync([Record(1, "AA:BB:CC:DD:EE:01", RecordedAt)], CancellationToken.None)).Single();

        outcome.Status.Should().Be(EdgeOutboxSendStatus.PermanentFailure);
        outcome.Error.Should().Be("Central ingest rejected the record: Pack voltage must be positive");
    }

    private static JkBmsOutboxBatchSender Sender(StubHandler handler) => new(
        new StubHttpClientFactory(handler),
        new JkBmsCentralIngestCredential { ApiKey = "central-key" },
        Options.Create(new JkBmsOptions { CentralIngestEndpoint = "https://central.test/api/v1/bms/readings" }));

    private static EdgeOutboxRecord Record(long id, string address, DateTime recordedAt) => new()
    {
        Id = id,
        SourceId = address,
        DeviceId = $"bank-{id}",
        PayloadType = EdgePayloadTypes.BmsReading,
        PayloadVersion = "1",
        RecordedAtUtc = recordedAt,
        PayloadJson = JsonSerializer.Serialize(BmsOutboxWriterTests.Bundle(recordedAt, address), JsonSerializerOptions.Web),
    };

    private static HttpResponseMessage Created(string json) => new(HttpStatusCode.Created)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            ApiKey = request.Headers.GetValues("X-Api-Key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
