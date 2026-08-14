using System.Net;
using System.Text;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace HVO.Hardware.DavisVantagePro2.WeatherUnderground;

internal sealed class WeatherUndergroundClient(
    HttpClient httpClient,
    IOptions<WeatherUndergroundOptions> options,
    TimeProvider timeProvider)
{
    public async Task<WeatherUndergroundSendResult> SendAsync(
        string stationKey,
        Loop2Packet observation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            WeatherUndergroundQueryBuilder.BuildRelativeUri(
                options.Value.StationId,
                stationKey,
                observation,
                options.Value.IntervalSeconds));
        using var timeout = new CancellationTokenSource(options.Value.RequestTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var suppression = SuppressInstrumentationScope.Begin();
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var retryable = response.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                return new(false, retryable, WeatherUndergroundOutcome.HttpRejected);
            }

            var accepted = await HasSuccessBodyAsync(response.Content, linked.Token).ConfigureAwait(false);
            return accepted
                ? WeatherUndergroundSendResult.Success
                : new(false, false, WeatherUndergroundOutcome.ResponseRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(false, true, WeatherUndergroundOutcome.Timeout);
        }
        catch (HttpRequestException)
        {
            return new(false, true, WeatherUndergroundOutcome.Transport);
        }
        catch (IOException)
        {
            return new(false, true, WeatherUndergroundOutcome.Transport);
        }
    }

    private static async Task<bool> HasSuccessBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[64];
        var length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        var body = Encoding.ASCII.GetString(buffer, 0, length).Trim();
        return string.Equals(body, "success", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct WeatherUndergroundSendResult(
    bool Succeeded,
    bool Retryable,
    WeatherUndergroundOutcome Outcome)
{
    public static WeatherUndergroundSendResult Success { get; } = new(true, false, WeatherUndergroundOutcome.Success);
}

internal enum WeatherUndergroundOutcome
{
    Success,
    NoReading,
    StaleObservation,
    Timeout,
    Transport,
    HttpRejected,
    ResponseRejected,
}

internal static class WeatherUndergroundOutcomeExtensions
{
    public static string Category(this WeatherUndergroundOutcome outcome) => outcome switch
    {
        WeatherUndergroundOutcome.Success => "success",
        WeatherUndergroundOutcome.NoReading => "no-reading",
        WeatherUndergroundOutcome.StaleObservation => "stale-observation",
        WeatherUndergroundOutcome.Timeout => "timeout",
        WeatherUndergroundOutcome.Transport => "transport",
        WeatherUndergroundOutcome.HttpRejected => "http-rejected",
        WeatherUndergroundOutcome.ResponseRejected => "response-rejected",
        _ => "unknown",
    };
}
