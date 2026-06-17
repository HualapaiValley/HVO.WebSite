using System.Net;
using System.Text;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Outbox.Forwarders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.Outbox;

[TestClass]
public sealed class HttpApiForwarderTests
{
    [TestMethod]
    public async Task ForwardAsync_OnBadRequest_ThrowsHttpRequestException()
    {
        var forwarder = CreateForwarder(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad payload"),
        });
        var batch = CreateBatch();

        var act = () => forwarder.ForwardAsync(batch, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [TestMethod]
    public async Task ForwardAsync_OnAuthOrRoutingFailure_ThrowsHttpRequestException()
    {
        foreach (var statusCode in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            var forwarder = CreateForwarder(_ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("configuration error"),
            });

            var act = () => forwarder.ForwardAsync(CreateBatch(), CancellationToken.None);

            await act.Should().ThrowAsync<HttpRequestException>();
        }
    }

    [TestMethod]
    public async Task ForwardAsync_OnTransientHttpStatus_ThrowsHttpRequestException()
    {
        var forwarder = CreateForwarder(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("server error"),
        });

        var act = () => forwarder.ForwardAsync(CreateBatch(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [TestMethod]
    public async Task ForwardAsync_OnBatchFailure_MapsFailureToMatchingRecordOnly()
    {
        var responseJson = """
            {
              "inserted": 1,
              "skipped": 0,
              "failed": [
                {
                  "deviceAddress": "AA:BB:CC:DD:EE:02",
                  "recordedAtUtc": "2026-06-16T12:00:01Z",
                  "error": "invalid reading"
                }
              ]
            }
            """;
        var forwarder = CreateForwarder(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });

        var act = () => forwarder.ForwardAsync(CreateBatch(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PermanentForwarderException>();
        ex.Which.FailedRecords.Should().ContainSingle().Which.Should().Be((2L, "invalid reading"));
    }

    private static HttpApiForwarder CreateForwarder(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var options = Options.Create(new OutboxOptions
        {
            ApiEndpoint = "https://example.test/api/v1/bms/readings",
            ApiKey = "test-key",
        });
        return new HttpApiForwarder(
            new TestHttpClientFactory(new TestHandler(responder)),
            options,
            NullLogger<HttpApiForwarder>.Instance);
    }

    private static List<EdgeOutboxRecord> CreateBatch() =>
    [
        new()
        {
            Id = 1,
            SourceId = "AA:BB:CC:DD:EE:01",
            DeviceId = "bank-1a",
            PayloadType = BmsOutboxPayloadTypes.Reading,
            PayloadVersion = BmsOutboxPayloadTypes.ReadingVersion,
            RecordedAtUtc = new DateTime(2026, 06, 16, 12, 00, 00, DateTimeKind.Utc),
            PayloadJson = "{}",
        },
        new()
        {
            Id = 2,
            SourceId = "AA:BB:CC:DD:EE:02",
            DeviceId = "bank-1b",
            PayloadType = BmsOutboxPayloadTypes.Reading,
            PayloadVersion = BmsOutboxPayloadTypes.ReadingVersion,
            RecordedAtUtc = new DateTime(2026, 06, 16, 12, 00, 01, DateTimeKind.Utc),
            PayloadJson = "{}",
        },
    ];

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
