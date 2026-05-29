using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Hosting;

public sealed class KasaGatewayWorker(
    IOptions<KasaGatewayOptions> options,
    KasaDevicePoller poller,
    KasaGatewayState state,
    ILogger<KasaGatewayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        state.MarkPollStarted();

        try
        {
            foreach (var device in options.Value.Devices.Where(device => device.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await poller.PollReadOnlyAsync(device, options.Value.DefaultPort, cancellationToken).ConfigureAwait(false);
                state.ApplyResult(device, result);

                if (!result.IsSuccess)
                {
                    logger.LogWarning(
                        "TP-Link/Kasa device {SourceId} poll failed: {FailureReason}",
                        device.EffectiveSourceId,
                        result.FailureReason);
                }
                else if (result.IsDegraded)
                {
                    logger.LogWarning(
                        "TP-Link/Kasa device {SourceId} poll degraded: {DegradedReason}",
                        device.EffectiveSourceId,
                        result.DegradedReason);
                }
            }

            state.MarkPollCompleted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.MarkPollFailed("Gateway polling failed.");
            logger.LogError(ex, "TP-Link/Kasa gateway polling failed.");
        }
    }
}
