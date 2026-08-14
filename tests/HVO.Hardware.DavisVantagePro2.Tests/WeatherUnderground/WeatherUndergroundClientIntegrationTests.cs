using System.Net;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.WeatherUnderground;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HVO.Hardware.DavisVantagePro2.Tests.WeatherUnderground;

[TestClass]
public sealed class WeatherUndergroundClientIntegrationTests
{
    [TestMethod]
    public async Task SendAsync_UsesLoopbackServerAndAcceptsSuccessResponse()
    {
        var receivedQuery = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.MapGet("/weatherstation/updateweatherstation.php", async context =>
        {
            receivedQuery.TrySetResult(context.Request.QueryString.Value ?? "");
            await context.Response.WriteAsync("success\n");
        });
        await server.StartAsync();
        var address = server.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{address}/weatherstation/updateweatherstation.php"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var client = CreateClient(httpClient, TimeProvider.System);
        var result = await client.SendAsync(
            "synthetic-loopback-key",
            new Loop2Packet
            {
                RecordedAtUtc = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc),
                OutsideTemperatureF = 82,
            },
            CancellationToken.None);

        result.Should().Be(WeatherUndergroundSendResult.Success);
        var query = await receivedQuery.Task.WaitAsync(TimeSpan.FromSeconds(5));
        query.Should().Contain("ID=KAZKINGM12");
        query.Should().Contain("tempf=82");
        await server.StopAsync();
    }

    [TestMethod]
    public async Task SendAsync_TimeoutReturnsSanitizedRetryableFailure()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpClient = new HttpClient(new StubHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }))
        {
            BaseAddress = new Uri("https://example.test/weatherstation/updateweatherstation.php"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var client = CreateClient(httpClient, clock);

        var send = client.SendAsync(
            "synthetic-timeout-key",
            new Loop2Packet { RecordedAtUtc = clock.GetUtcNow().UtcDateTime },
            CancellationToken.None);
        await entered.Task;
        clock.Advance(TimeSpan.FromSeconds(2));

        var result = await send;
        result.Succeeded.Should().BeFalse();
        result.Retryable.Should().BeTrue();
        result.Outcome.Should().Be(WeatherUndergroundOutcome.Timeout);
    }

    [TestMethod]
    public async Task SendAsync_CallerCancellationPropagates()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpClient = new HttpClient(new StubHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }))
        {
            BaseAddress = new Uri("https://example.test/weatherstation/updateweatherstation.php"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var client = CreateClient(httpClient, TimeProvider.System);
        using var cancellation = new CancellationTokenSource();

        var send = client.SendAsync(
            "synthetic-cancellation-key",
            new Loop2Packet { RecordedAtUtc = DateTime.UtcNow },
            cancellation.Token);
        await entered.Task;
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => send).Should().ThrowAsync<OperationCanceledException>();
    }

    private static WeatherUndergroundClient CreateClient(HttpClient httpClient, TimeProvider timeProvider) => new(
        httpClient,
        Options.Create(new WeatherUndergroundOptions
        {
            Enabled = true,
            StationId = "KAZKINGM12",
            IntervalSeconds = 5,
            RequestTimeoutSeconds = 2,
        }),
        timeProvider);

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
