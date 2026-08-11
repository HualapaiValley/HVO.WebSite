using System.Net;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.Weather;
using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public sealed class DavisOutboxBatchSenderTests
{
    [TestMethod]
    public async Task SendAsync_PartitionsMixedPayloadsAndAccountsEveryRecord()
    {
        var paths = new List<string>();
        using var client = new HttpClient(new StubHandler(async request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            var payload = await request.Content!.ReadAsStringAsync();
            payload.Should().Contain("station-1");
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"inserted\":1,\"skipped\":0,\"failed\":[]}")
            };
        }));
        var sender = CreateSender(client);
        var at = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var live = new DavisWeatherLivePayload { StationId = "station-1", RecordedAtUtc = at, TemperatureF = 72.5 };
        var archive = CompleteArchive(at.AddMinutes(-5));

        var outcomes = await sender.SendAsync([
            Record(1, DavisOutboxPayloadTypes.Raw, at, live),
            Record(2, DavisOutboxPayloadTypes.Archive, archive.RecordedAtUtc, archive),
        ], CancellationToken.None);

        paths.Should().BeEquivalentTo(["/api/v1/weather/raw/batch", "/api/v1/weather/archive/batch"]);
        outcomes.Should().HaveCount(2).And.OnlyContain(outcome => outcome.Status == EdgeOutboxSendStatus.Sent);
    }

    [TestMethod]
    public async Task SendAsync_InvalidAccountingRetriesOnlyAffectedPartition()
    {
        using var client = new HttpClient(new StubHandler(request => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("archive", StringComparison.Ordinal)
                    ? "{\"inserted\":0,\"skipped\":0,\"failed\":[]}"
                    : "{\"inserted\":1,\"skipped\":0,\"failed\":[]}")
            })));
        var sender = CreateSender(client);
        var at = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var archive = CompleteArchive(at.AddMinutes(-5));

        var outcomes = await sender.SendAsync([
            Record(1, DavisOutboxPayloadTypes.Raw, at, new DavisWeatherLivePayload { StationId = "station-1", RecordedAtUtc = at }),
            Record(2, DavisOutboxPayloadTypes.Archive, archive.RecordedAtUtc, archive),
        ], CancellationToken.None);

        outcomes.Single(outcome => outcome.RecordId == 1).Status.Should().Be(EdgeOutboxSendStatus.Sent);
        outcomes.Single(outcome => outcome.RecordId == 2).Status.Should().Be(EdgeOutboxSendStatus.TransientFailure);
    }

    [TestMethod]
    public void ArchiveContract_RoundTripsEveryParsedField()
    {
        var expected = CompleteArchive(new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc));
        var actual = JsonSerializer.Deserialize<DavisWeatherArchivePayload>(
            JsonSerializer.Serialize(expected, JsonSerializerOptions.Web), JsonSerializerOptions.Web);
        actual.Should().BeEquivalentTo(expected);
    }

    private static DavisOutboxBatchSender CreateSender(HttpClient client) => new(
        new StubFactory(client),
        new DavisCentralIngestCredential { ApiKey = "secret" },
        Options.Create(new StationOptions { CentralIngestBaseEndpoint = "https://example.test/" }));

    private static EdgeOutboxRecord Record<T>(long id, string type, DateTime at, T payload) => new()
    {
        Id = id,
        SourceId = "station-1",
        PayloadType = type,
        PayloadVersion = "1",
        RecordedAtUtc = at,
        PayloadJson = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
    };

    private static DavisWeatherArchivePayload CompleteArchive(DateTime utc) => new()
    {
        StationId = "station-1", RecordedAtUtc = utc,
        ConsoleRecordedAtLocal = DateTime.SpecifyKind(utc.AddHours(-7), DateTimeKind.Unspecified),
        ArchiveIntervalMinutes = 5, TemperatureF = 70, HighTemperatureF = 72, LowTemperatureF = 68,
        InsideTemperatureF = 75, HumidityPercent = 30, InsideHumidityPercent = 35,
        BarometricPressureInHg = 29.9, WindSpeedMph = 4, WindGustMph = 9,
        WindDirectionDegrees = 180, WindGustDirectionDegrees = 202.5, WindSamples = 150,
        RainfallInches = .01, RainRateInchesPerHour = .1, SolarRadiationWm2 = 600,
        HighSolarRadiationWm2 = 700, UvIndex = 4, HighUvIndex = 5, EtInches = .002,
        ForecastRule = 6, ForecastString = "Mostly clear", DownloadRecordType = 1,
        LeafTemp1F = 65, LeafTemp2F = 66, LeafWetnessScaled = [1, null],
        SoilTemperaturesF = [60, 61, null, 63], ExtraHumiditiesPercent = [40, null],
        ExtraTemperaturesF = [64, null, 66], SoilMoisturesCb = [10, 20, null, 40],
    };

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
