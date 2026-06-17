using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantRestClientTests
{
    [TestMethod]
    public async Task GetMetricsAsync_BlankHostReturnsEmptyListWithoutHttpCall()
    {
        var factory = new FakeHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be called."));
        var client = CreateClient(factory, new SolarAssistantOptions { Host = string.Empty });

        var metrics = await client.GetMetricsAsync(CancellationToken.None);

        metrics.Should().BeEmpty();
        factory.CreatedClients.Should().Be(0);
    }

    [TestMethod]
    public async Task GetMetricsAsync_BearerTokenPreferredOverBasicAuth()
    {
        HttpRequestMessage? request = null;
        var client = CreateClient(new FakeHttpClientFactory(r =>
        {
            request = r;
            return JsonResponse("[]");
        }), new SolarAssistantOptions
        {
            Host = "solarassistant.local",
            BearerToken = "bearer-token",
            RestUsername = "admin",
            RestPassword = "basic-password",
        });

        await client.GetMetricsAsync(CancellationToken.None);

        request.Should().NotBeNull();
        request!.Headers.Authorization.Should().BeEquivalentTo(new AuthenticationHeaderValue("Bearer", "bearer-token"));
    }

    [TestMethod]
    public async Task GetMetricsAsync_BasicAuthUsesConfiguredCredentials()
    {
        HttpRequestMessage? request = null;
        var client = CreateClient(new FakeHttpClientFactory(r =>
        {
            request = r;
            return JsonResponse("[]");
        }), new SolarAssistantOptions
        {
            Host = "solarassistant.local",
            RestUsername = "sa-user",
            RestPassword = "sa-password",
        });

        await client.GetMetricsAsync(CancellationToken.None);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("sa-user:sa-password"));
        request.Should().NotBeNull();
        request!.Headers.Authorization.Should().BeEquivalentTo(new AuthenticationHeaderValue("Basic", expected));
    }

    [TestMethod]
    public async Task GetMetricsAsync_NonSuccessThrowsWithBoundedErrorBody()
    {
        var logger = new CapturingLogger<SolarAssistantRestClient>();
        var client = CreateClient(new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(new string('x', 700)),
        }), new SolarAssistantOptions { Host = "solarassistant.local" }, logger);

        var act = () => client.GetMetricsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        logger.Messages.Should().ContainSingle(message =>
            message.Contains("HTTP 500", StringComparison.Ordinal) &&
            message.Contains(new string('x', 512), StringComparison.Ordinal) &&
            !message.Contains(new string('x', 513), StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetMetricsAsync_ValidJsonMapsMetrics()
    {
        var client = CreateClient(new FakeHttpClientFactory(_ => JsonResponse("""
            [
              {"topic":"total/pv_power","group":"total","name":"PV power","value":1234,"unit":"W"},
              {"topic":"total/battery_soc","group":"total","name":"Battery SOC","value":88.5,"unit":"%"}
            ]
            """)), new SolarAssistantOptions { Host = "solarassistant.local" });

        var metrics = await client.GetMetricsAsync(CancellationToken.None);

        metrics.Should().HaveCount(2);
        metrics[0].Topic.Should().Be("total/pv_power");
        metrics[0].Group.Should().Be("total");
        metrics[0].Name.Should().Be("PV power");
        metrics[0].Unit.Should().Be("W");
        metrics[0].Value.Should().NotBeNull();
        metrics[1].Topic.Should().Be("total/battery_soc");
        metrics[1].Unit.Should().Be("%");
    }

    private static SolarAssistantRestClient CreateClient(
        IHttpClientFactory factory,
        SolarAssistantOptions options,
        ILogger<SolarAssistantRestClient>? logger = null) =>
        new(factory, Options.Create(options), logger ?? new CapturingLogger<SolarAssistantRestClient>());

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public int CreatedClients { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreatedClients++;
            return new HttpClient(new DelegatingHandlerStub(_handler));
        }
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
