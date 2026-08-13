using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HVO.Edge.Outbox;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
public sealed class HomeAssistantOutboxBatchSenderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task SendAsync_RoutesContractsAndAddsApiKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        var sender = CreateSender(handler);

        var outcomes = await sender.SendAsync([
            Record(1, HomeAssistantExportContract.PowerReading, new { sourceId = "kasa:1", recordedAtUtc = DateTime.UtcNow, loadPowerW = 10 }),
            Record(2, HomeAssistantExportContract.WeatherRaw, new { stationId = "govee:1", recordedAt = DateTime.UtcNow, temperatureF = 70 })
        ], CancellationToken.None);

        outcomes.Should().OnlyContain(outcome => outcome.Status == EdgeOutboxSendStatus.Sent);
        handler.Requests.Select(request => request.Path).Should().BeEquivalentTo("/api/v1/power/readings", "/api/v1/weather/raw/batch");
        handler.Requests.Should().OnlyContain(request => request.ApiKey == "central-key");
    }

    [TestMethod]
    public async Task SendAsync_ClassifiesTransientAndInvalidPayloadFailures()
    {
        var sender = CreateSender(new RecordingHandler(HttpStatusCode.ServiceUnavailable));

        var outcomes = await sender.SendAsync([
            Record(1, HomeAssistantExportContract.PowerReading, new { value = 1 }),
            new EdgeOutboxRecord { Id = 2, PayloadJson = "not-json" }
        ], CancellationToken.None);

        outcomes.Single(item => item.RecordId == 1).Status.Should().Be(EdgeOutboxSendStatus.TransientFailure);
        outcomes.Single(item => item.RecordId == 2).Status.Should().Be(EdgeOutboxSendStatus.PermanentFailure);
    }

    [TestMethod]
    public async Task SendAsync_MapsHttpSuccessPerRecordFailuresToPermanentOutcome()
    {
        var timestamp = DateTime.Parse("2026-08-11T10:00:00Z").ToUniversalTime();
        var handler = new RecordingHandler(
            HttpStatusCode.Created,
            $$"""{"inserted":0,"skipped":0,"failed":[{"recordedAtUtc":"{{timestamp:O}}","error":"invalid value"}]}""");
        var sender = CreateSender(handler);
        var record = Record(1, HomeAssistantExportContract.PowerReading, new { sourceId = "kasa:1", recordedAtUtc = timestamp, loadPowerW = 10 });
        record.SourceId = "kasa:1";
        record.RecordedAtUtc = timestamp;

        var outcomes = await sender.SendAsync([record], CancellationToken.None);

        outcomes.Should().ContainSingle().Which.Status.Should().Be(EdgeOutboxSendStatus.PermanentFailure);
    }

    [TestMethod]
    public async Task SendAsync_TreatsUnknownFailureTimestampAsTransientContractError()
    {
        var timestamp = DateTime.Parse("2026-08-11T10:00:00Z").ToUniversalTime();
        var unknown = timestamp.AddMinutes(1);
        var handler = new RecordingHandler(
            HttpStatusCode.Created,
            $$"""{"inserted":0,"skipped":0,"failed":[{"recordedAtUtc":"{{unknown:O}}","error":"invalid value"}]}""");
        var sender = CreateSender(handler);
        var record = Record(1, HomeAssistantExportContract.PowerReading, new { sourceId = "kasa:1", recordedAtUtc = timestamp, loadPowerW = 10 });
        record.SourceId = "kasa:1";
        record.RecordedAtUtc = timestamp;

        var outcomes = await sender.SendAsync([record], CancellationToken.None);

        outcomes.Should().ContainSingle().Which.Status.Should().Be(EdgeOutboxSendStatus.TransientFailure);
    }

    private static HomeAssistantOutboxBatchSender CreateSender(RecordingHandler handler) => new(
        new StubFactory(new HttpClient(handler)),
        new HomeAssistantExporterCredential { CentralApiKey = "central-key" },
        Options.Create(TestOptions.Create()));

    private static EdgeOutboxRecord Record(long id, HomeAssistantExportContract contract, object payload) => new()
    {
        Id = id,
        PayloadJson = JsonSerializer.Serialize(new HomeAssistantOutboxPayload(
            contract, JsonSerializer.SerializeToElement(payload, JsonOptions)), JsonOptions)
    };

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string? content = null) : HttpMessageHandler
    {
        public List<(string Path, string? ApiKey)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.GetValues("X-Api-Key").Single()));
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content ?? "{\"inserted\":1,\"skipped\":0,\"failed\":[]}")
            });
        }
    }
}
