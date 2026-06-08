using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Protocol;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaPlugControlLab(
    KasaLegacyLabClient client,
    KasaSystemInfoParser parser,
    KasaIdentityValidator identityValidator)
{
    public async Task<KasaPlugLabRun> RunAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);

        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var startState = baseline.Info.RelayState;
        if (startState is not 0 and not 1)
        {
            throw new InvalidOperationException("Desk Lamp did not expose a top-level relay_state; refusing plug lab writes.");
        }

        var desiredStates = BuildDesiredStates(options.Target, startState.Value);
        var observations = new List<KasaPlugLabObservation>
        {
            new("baseline", null, startState == 1, baseline.Elapsed)
        };

        foreach (var desiredState in desiredStates)
        {
            var writeElapsed = await SendRelayStateAsync(config.Host, config.EffectivePort(9999), desiredState, cancellationToken).ConfigureAwait(false);
            var readback = await WaitForRelayStateAsync(config, desiredState, options.ReadbackTimeout, options.ReadbackPollInterval, cancellationToken).ConfigureAwait(false);
            observations.Add(new(
                desiredState == 1 ? "set-on" : "set-off",
                writeElapsed,
                readback.Info.RelayState == 1,
                readback.Elapsed));
        }

        return new KasaPlugLabRun(
            baseline.Info.Model,
            baseline.Info.HardwareVersion,
            baseline.Info.SoftwareVersion,
            startState == 1,
            observations);
    }

    private async Task<KasaPlugLabRead> ReadValidatedAsync(KasaDeviceConfig config, CancellationToken cancellationToken)
    {
        var read = await SendAsync(config.Host, config.EffectivePort(9999), KasaCommands.GetSystemInfo, cancellationToken).ConfigureAwait(false);
        using var response = read.Response;
        var info = parser.Parse(response);
        var validation = identityValidator.Validate(config, info);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Reason ?? "Desk Lamp identity validation failed.");
        }

        if (!string.Equals(info.Model, config.ExpectedModel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Desk Lamp model validation failed.");
        }

        if (info.Children.Count > 0)
        {
            throw new InvalidOperationException("Desk Lamp plug lab only supports single-relay plugs, not child outlet devices.");
        }

        return new KasaPlugLabRead(info, read.Elapsed);
    }

    private async Task<KasaPlugLabRead> WaitForRelayStateAsync(KasaDeviceConfig config, int expectedState, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        KasaPlugLabRead? lastRead = null;
        do
        {
            lastRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
            if (lastRead.Info.RelayState == expectedState)
            {
                return new KasaPlugLabRead(lastRead.Info, stopwatch.Elapsed);
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
        while (stopwatch.Elapsed < timeout);

        throw new TimeoutException($"Desk Lamp did not report relay_state={expectedState.ToString(CultureInfo.InvariantCulture)} within {timeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms. Last observed relay_state={lastRead?.Info.RelayState?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.");
    }

    private async Task<TimeSpan> SendRelayStateAsync(string host, int port, int state, CancellationToken cancellationToken)
    {
        var command = string.Create(CultureInfo.InvariantCulture, $"{{\"system\":{{\"set_relay_state\":{{\"state\":{state}}}}}}}");
        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(host, port, command, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private async Task RestoreRelayOnForCleanupAsync(KasaDeviceConfig config)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cleanupTimeout.Token).ConfigureAwait(false);
    }

    private async Task<KasaPlugLabReadResponse> SendAsync(string host, int port, string command, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await client.SendAsync(host, port, command, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new KasaPlugLabReadResponse(response, stopwatch.Elapsed);
    }

    private static IReadOnlyList<int> BuildDesiredStates(string target, int baselineState) =>
        target.ToLowerInvariant() switch
        {
            "on" => [1],
            "off" => [0],
            "toggle" => [baselineState == 1 ? 0 : 1],
            "cycle" => baselineState == 1 ? [0, 1] : [1, 0],
            _ => throw new ArgumentOutOfRangeException(nameof(target), "Target must be on, off, toggle, or cycle.")
        };

    public async Task<KasaPlugExerciseRun> RunExerciseAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var steps = new List<KasaPlugExerciseStep>();
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var originalRelayState = baseline.Info.RelayState == 1;

        steps.Add(KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(originalRelayState ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed));

        await ExerciseAliasAsync(config, originalAlias, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseLedAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseTimeAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseMeterCommandsAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseRuleReadsAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseScheduleFireAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseCountdownFireAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseRebootAsync(config, steps, cancellationToken).ConfigureAwait(false);

        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        if (finalRead.Info.RelayState == 0)
        {
            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
            finalRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(finalRead.Info.Alias, originalAlias, StringComparison.Ordinal))
        {
            await SendCommandAsync(config, BuildAliasCommand(originalAlias), cancellationToken).ConfigureAwait(false);
            finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        }

        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(
            finalRead.Info.Model,
            finalRead.Info.HardwareVersion,
            finalRead.Info.SoftwareVersion,
            originalAlias,
            finalRead.Info.Alias,
            finalRead.Info.RelayState == 1,
            steps);
    }

    public async Task<KasaPlugExerciseRun> RunScheduleOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseScheduleFireAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunSchedulePairOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseSchedulePairFireAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunScheduleRoundtripOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseAppScheduleRoundtripAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunScheduleClearOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseScheduleClearAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunCountdownOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseCountdownFireAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunCountdownOnOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseCountdownOnFireAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunAppCountdownOffOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseAppCountdownOffAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunAppCountdownOnOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseAppCountdownOnAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunAppCountdownOnFromOffOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseAppCountdownOnFromOffAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunInspectOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, on_time={baseline.Info.OnTimeSeconds?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}s, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await InspectRulesAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await InspectRuntimeAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, on_time={finalRead.Info.OnTimeSeconds?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}s, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunLedOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, led_off={GetInt(baseline.Info.RawSystemInfo, "led_off")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExerciseLedAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, led_off={GetInt(finalRead.Info.RawSystemInfo, "led_off")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunBrightnessOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var originalRelayState = baseline.Info.RelayState == 1;
        var originalBrightness = GetInt(baseline.Info.RawSystemInfo, "brightness");
        var testBrightness = originalBrightness is null or < 75 ? 90 : 40;
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(originalRelayState ? "On" : "Off")}, brightness={originalBrightness?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        if (originalBrightness is null)
        {
            steps.Add(KasaPlugExerciseStep.Skipped("brightness", "sysinfo did not expose a brightness field; skipped dimmer write", TimeSpan.Zero));
            return new KasaPlugExerciseRun(baseline.Info.Model, baseline.Info.HardwareVersion, baseline.Info.SoftwareVersion, originalAlias, baseline.Info.Alias, originalRelayState, steps);
        }

        var set = await SendCommandAsync(config, BuildSetBrightnessCommand(testBrightness), cancellationToken).ConfigureAwait(false);
        var changed = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        try
        {
            var changedBrightness = GetInt(changed.Info.RawSystemInfo, "brightness");
            var restoreBrightness = await SendCommandAsync(config, BuildSetBrightnessCommand(originalBrightness.Value), cancellationToken).ConfigureAwait(false);
            var restoreStateElapsed = TimeSpan.Zero;
            if (originalRelayState != (changed.Info.RelayState == 1))
            {
                restoreStateElapsed = await SendRelayStateAsync(config.Host, config.EffectivePort(9999), originalRelayState ? 1 : 0, cancellationToken).ConfigureAwait(false);
            }

            var restored = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
            steps.Add(KasaPlugExerciseStep.Ok(
                "brightness",
                $"target={testBrightness}, set err={GetErrCode(set.Response, "smartlife.iot.dimmer", "set_brightness")}, changed brightness={changedBrightness?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, restore err={GetErrCode(restoreBrightness.Response, "smartlife.iot.dimmer", "set_brightness")}, restored brightness={GetInt(restored.Info.RawSystemInfo, "brightness")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, restored relay={(restored.Info.RelayState == 1 ? "On" : "Off")}",
                set.Elapsed + changed.Elapsed + restoreBrightness.Elapsed + restoreStateElapsed + restored.Elapsed));
            restoreBrightness.Response.Dispose();
        }
        finally
        {
            set.Response.Dispose();
        }

        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, brightness={GetInt(finalRead.Info.RawSystemInfo, "brightness")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunWatchOnOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        var onRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("watch-on", $"relay reached On after {onRead.Elapsed.TotalSeconds:0.0}s", onRead.Elapsed));
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunPowerCycleOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await ExercisePrearmedPowerCycleAsync(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    public async Task<KasaPlugExerciseRun> RunScheduleOneTimeOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken) =>
        await RunExerciseTargetAsync(options, ExerciseAppOneTimeScheduleFireAsync, cancellationToken).ConfigureAwait(false);

    public async Task<KasaPlugExerciseRun> RunScheduleRebootOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken) =>
        await RunExerciseTargetAsync(options, ExerciseScheduleRebootPersistenceAsync, cancellationToken).ConfigureAwait(false);

    public async Task<KasaPlugExerciseRun> RunAwayOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken) =>
        await RunExerciseTargetAsync(options, ExerciseAwayRuleRoundtripAsync, cancellationToken).ConfigureAwait(false);

    public async Task<KasaPlugExerciseRun> RunCountdownRebootOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken) =>
        await RunExerciseTargetAsync(options, ExerciseCountdownRebootPersistenceAsync, cancellationToken).ConfigureAwait(false);

    public async Task<KasaPlugExerciseRun> RunDiagnosticsOnlyAsync(KasaPlugLabOptions options, CancellationToken cancellationToken) =>
        await RunExerciseTargetAsync(options, ExerciseDiagnosticsAsync, cancellationToken).ConfigureAwait(false);

    private async Task<KasaPlugExerciseRun> RunExerciseTargetAsync(KasaPlugLabOptions options, Func<KasaDeviceConfig, List<KasaPlugExerciseStep>, CancellationToken, Task> exercise, CancellationToken cancellationToken)
    {
        var config = BuildConfig(options);
        var baseline = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalAlias = baseline.Info.Alias ?? "Desk Lamp";
        var steps = new List<KasaPlugExerciseStep>
        {
            KasaPlugExerciseStep.Ok("baseline", $"alias='{originalAlias}', relay={(baseline.Info.RelayState == 1 ? "On" : "Off")}, rssi={baseline.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", baseline.Elapsed)
        };

        await exercise(config, steps, cancellationToken).ConfigureAwait(false);
        var finalRead = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("final", $"alias='{finalRead.Info.Alias}', relay={(finalRead.Info.RelayState == 1 ? "On" : "Off")}, rssi={finalRead.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", finalRead.Elapsed));

        return new KasaPlugExerciseRun(finalRead.Info.Model, finalRead.Info.HardwareVersion, finalRead.Info.SoftwareVersion, originalAlias, finalRead.Info.Alias, finalRead.Info.RelayState == 1, steps);
    }

    private static KasaDeviceConfig BuildConfig(KasaPlugLabOptions options) =>
        new()
        {
            Enabled = true,
            DeviceId = options.DeviceId,
            SourceId = string.IsNullOrWhiteSpace(options.SourceId) ? "tplink-kasa:plug-lab" : options.SourceId,
            Host = options.Host,
            Port = options.Port,
            MacAddress = options.MacAddress,
            ExpectedModel = options.ExpectedModel,
            DeviceKind = KasaDeviceKind.Plug
        };

    private async Task ExerciseAliasAsync(KasaDeviceConfig config, string originalAlias, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var temporaryAlias = originalAlias.EndsWith(" Lab", StringComparison.Ordinal) ? $"{originalAlias} 2" : $"{originalAlias} Lab";
        var set = await SendCommandAsync(config, BuildAliasCommand(temporaryAlias), cancellationToken).ConfigureAwait(false);
        var changed = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var restore = await SendCommandAsync(config, BuildAliasCommand(originalAlias), cancellationToken).ConfigureAwait(false);
        var restored = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var ok = string.Equals(changed.Info.Alias, temporaryAlias, StringComparison.Ordinal)
            && string.Equals(restored.Info.Alias, originalAlias, StringComparison.Ordinal);
        steps.Add(new KasaPlugExerciseStep(
            "alias",
            ok,
            $"set err={GetErrCode(set.Response, "system", "set_dev_alias")}, changed='{changed.Info.Alias}', restore err={GetErrCode(restore.Response, "system", "set_dev_alias")}, restored='{restored.Info.Alias}'",
            set.Elapsed + changed.Elapsed + restore.Elapsed + restored.Elapsed));
        set.Response.Dispose();
        restore.Response.Dispose();
    }

    private async Task ExerciseLedAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var read = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var originalOff = GetInt(read.Info.RawSystemInfo, "led_off");
        if (originalOff is not 0 and not 1)
        {
            steps.Add(KasaPlugExerciseStep.Skipped("led", "sysinfo led_off is unavailable", read.Elapsed));
            return;
        }

        var toggled = originalOff == 1 ? 0 : 1;
        var set = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"system\":{{\"set_led_off\":{{\"off\":{toggled}}}}}}}"), cancellationToken).ConfigureAwait(false);
        var changed = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var restore = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"system\":{{\"set_led_off\":{{\"off\":{originalOff.Value}}}}}}}"), cancellationToken).ConfigureAwait(false);
        var restored = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var changedOff = GetInt(changed.Info.RawSystemInfo, "led_off");
        var restoredOff = GetInt(restored.Info.RawSystemInfo, "led_off");
        steps.Add(new KasaPlugExerciseStep(
            "led",
            GetErrCode(set.Response, "system", "set_led_off") == 0 && changedOff == toggled && GetErrCode(restore.Response, "system", "set_led_off") == 0 && restoredOff == originalOff,
            $"original off={originalOff}, set err={GetErrCode(set.Response, "system", "set_led_off")}, changed off={changedOff?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, restore err={GetErrCode(restore.Response, "system", "set_led_off")}, restored off={restoredOff?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            read.Elapsed + set.Elapsed + changed.Elapsed + restore.Elapsed + restored.Elapsed));
        set.Response.Dispose();
        restore.Response.Dispose();
    }

    private async Task ExerciseTimeAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var time = await SendCommandAsync(config, "{\"time\":{\"get_time\":{}}}", cancellationToken).ConfigureAwait(false);
        var timezone = await SendCommandAsync(config, "{\"time\":{\"get_timezone\":{}}}", cancellationToken).ConfigureAwait(false);
        var index = GetInt(timezone.Response, "time", "get_timezone", "index");
        var deviceTime = TryGetDeviceTime(time.Response, out var parsedTime) ? parsedTime : DateTime.Now;
        var setTime = await SendCommandAsync(config, BuildSetTimeCommand(deviceTime), cancellationToken).ConfigureAwait(false);
        KasaPlugLabReadResponse? setTimezone = null;
        if (index is >= 0)
        {
            setTimezone = await SendCommandAsync(config, BuildSetTimezoneCommand(deviceTime, index.Value), cancellationToken).ConfigureAwait(false);
        }

        steps.Add(KasaPlugExerciseStep.Ok("time", $"device={deviceTime:yyyy-MM-dd HH:mm:ss}, timezone index={index?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, set_time err={GetErrCode(setTime.Response, "time", "set_time")}, set_timezone err={(setTimezone is null ? "skipped" : GetErrCode(setTimezone.Response, "time", "set_timezone"))}", time.Elapsed + timezone.Elapsed + setTime.Elapsed + (setTimezone?.Elapsed ?? TimeSpan.Zero)));
        time.Response.Dispose();
        timezone.Response.Dispose();
        setTime.Response.Dispose();
        setTimezone?.Response.Dispose();
    }

    private async Task ExerciseMeterCommandsAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var realtime = await SendCommandAsync(config, "{\"emeter\":{\"get_realtime\":{}}}", cancellationToken).ConfigureAwait(false);
        var eraseEnergy = await SendCommandAsync(config, "{\"emeter\":{\"erase_emeter_stat\":{}}}", cancellationToken).ConfigureAwait(false);
        var eraseRuntime = await SendCommandAsync(config, "{\"emeter\":{\"erase_runtime_stat\":{}}}", cancellationToken).ConfigureAwait(false);
        steps.Add(new KasaPlugExerciseStep(
            "meters",
            GetErrCode(eraseEnergy.Response, "emeter", "erase_emeter_stat") == 0 || GetErrCode(eraseRuntime.Response, "emeter", "erase_runtime_stat") == 0,
            $"get_realtime err={GetErrCode(realtime.Response, "emeter", "get_realtime")}, erase_emeter_stat err={GetErrCode(eraseEnergy.Response, "emeter", "erase_emeter_stat")}, erase_runtime_stat err={GetErrCode(eraseRuntime.Response, "emeter", "erase_runtime_stat")}",
            realtime.Elapsed + eraseEnergy.Elapsed + eraseRuntime.Elapsed));
        realtime.Response.Dispose();
        eraseEnergy.Response.Dispose();
        eraseRuntime.Response.Dispose();
    }

    private async Task ExerciseRuleReadsAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        foreach (var module in new[] { "schedule", "count_down", "anti_theft" })
        {
            var rules = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"{module}\":{{\"get_rules\":{{}}}}}}"), cancellationToken).ConfigureAwait(false);
            var next = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"{module}\":{{\"get_next_action\":{{}}}}}}"), cancellationToken).ConfigureAwait(false);
            steps.Add(KasaPlugExerciseStep.Ok(module, $"get_rules err={GetErrCode(rules.Response, module, "get_rules")}, rules={GetRuleCount(rules.Response, module)}, get_next_action err={GetErrCode(next.Response, module, "get_next_action")}", rules.Elapsed + next.Elapsed));
            rules.Response.Dispose();
            next.Response.Dispose();
        }
    }

    private async Task ExerciseScheduleFireAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var time = await SendCommandAsync(config, "{\"time\":{\"get_time\":{}}}", cancellationToken).ConfigureAwait(false);
        var deviceTime = TryGetDeviceTime(time.Response, out var parsedTime) ? parsedTime : DateTime.Now;
        time.Response.Dispose();
        var scheduledTime = deviceTime.AddMinutes(1);
        var scheduledMinute = scheduledTime.Hour * 60 + scheduledTime.Minute;
        var weekdays = BuildWeekdayMask(scheduledTime.DayOfWeek);
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var add = await SendCommandAsync(config, BuildScheduleRuleCommand(scheduledMinute, weekdays, 0), cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var createdRuleId = GetRuleIdByName(readback.Response, "schedule", "HVO Lab");
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(70), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-fire",
                    final.Info.RelayState == 1,
                    $"target minute={scheduledMinute}, add err={GetErrCode(add.Response, "schedule", "add_rule")}, created id={(createdRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "schedule")}, fired Off after {offRead.Elapsed.TotalSeconds:0.0}s, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "schedule", "delete_rule"))}, remained {(final.Info.RelayState == 1 ? "On" : "Off")} after cleanup watch",
                    add.Elapsed + readback.Elapsed + offRead.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                delete?.Response.Dispose();
            }
            catch (TimeoutException ex)
            {
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-fire",
                    false,
                    $"target minute={scheduledMinute}, add err={GetErrCode(add.Response, "schedule", "add_rule")}, created id={(createdRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "schedule")}, did not fire: {ex.Message}, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "schedule", "delete_rule"))}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    add.Elapsed + readback.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                delete?.Response.Dispose();
            }
        }
        finally
        {
            add.Response.Dispose();
            readback.Response.Dispose();
            if (createdRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseCountdownFireAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var add = await SendCommandAsync(config, "{\"count_down\":{\"add_rule\":{\"name\":\"HVO Lab\",\"enable\":1,\"delay\":30,\"act\":0}}}", cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var createdRuleId = GetRuleIdByName(readback.Response, "count_down", "HVO Lab");
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", createdRuleId), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "countdown-fire",
                    true,
                    $"delay=30s, add err={GetErrCode(add.Response, "count_down", "add_rule")}, created id={(createdRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "count_down")}, fired Off after {offRead.Elapsed.TotalSeconds:0.0}s, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "count_down", "delete_rule"))}",
                    add.Elapsed + readback.Elapsed + offRead.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero)));
                delete?.Response.Dispose();
            }
            catch (TimeoutException ex)
            {
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", createdRuleId), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "countdown-fire",
                    false,
                    $"delay=30s, add err={GetErrCode(add.Response, "count_down", "add_rule")}, created id={(createdRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "count_down")}, did not fire: {ex.Message}, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "count_down", "delete_rule"))}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    add.Elapsed + readback.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                delete?.Response.Dispose();
            }
        }
        finally
        {
            add.Response.Dispose();
            readback.Response.Dispose();
            if (createdRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", createdRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseCountdownOnFireAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var before = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var add = await SendCommandAsync(config, "{\"count_down\":{\"add_rule\":{\"name\":\"Timer AddTimerObject\",\"enable\":1,\"delay\":60,\"act\":1}}}", cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var next = await SendCommandAsync(config, "{\"count_down\":{\"get_next_action\":{}}}", cancellationToken).ConfigureAwait(false);
        var createdRuleId = GetNewRuleIdByName(before.Response, readback.Response, "count_down", "Timer AddTimerObject");
        try
        {
            var offElapsed = await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 0, cancellationToken).ConfigureAwait(false);
            try
            {
                var onRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", createdRuleId), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "countdown-on-fire",
                    true,
                    $"delay=60s, add err={GetErrCode(add.Response, "count_down", "add_rule")}, rules before={GetRuleCount(before.Response, "count_down")}, rules after={GetRuleCount(readback.Response, "count_down")}, new id={(createdRuleId is null ? "no" : "yes")}, next err={GetErrCode(next.Response, "count_down", "get_next_action")}, manual Off write={offElapsed.TotalMilliseconds:0}ms, restored On after {onRead.Elapsed.TotalSeconds:0.0}s, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "count_down", "delete_rule"))}",
                    before.Elapsed + add.Elapsed + readback.Elapsed + next.Elapsed + offElapsed + onRead.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero)));
                delete?.Response.Dispose();
            }
            catch (TimeoutException ex)
            {
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", createdRuleId), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "countdown-on-fire",
                    false,
                    $"delay=60s, add err={GetErrCode(add.Response, "count_down", "add_rule")}, rules before={GetRuleCount(before.Response, "count_down")}, rules after={GetRuleCount(readback.Response, "count_down")}, new id={(createdRuleId is null ? "no" : "yes")}, next err={GetErrCode(next.Response, "count_down", "get_next_action")}, did not fire: {ex.Message}, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "count_down", "delete_rule"))}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    before.Elapsed + add.Elapsed + readback.Elapsed + next.Elapsed + offElapsed + (delete?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                delete?.Response.Dispose();
            }
        }
        finally
        {
            before.Response.Dispose();
            add.Response.Dispose();
            readback.Response.Dispose();
            next.Response.Dispose();
            if (createdRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", createdRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseAppCountdownOffAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var started = await StartAppCountdownAsync(config, delaySeconds: 120, action: 0, cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-off",
                    true,
                    $"delay=120s, {SummarizeAppCountdownStart(started)}, fired Off after {offRead.Elapsed.TotalSeconds:0.0}s",
                    started.Elapsed + offRead.Elapsed));
            }
            catch (TimeoutException ex)
            {
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-off",
                    false,
                    $"delay=120s, {SummarizeAppCountdownStart(started)}, did not fire: {ex.Message}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    started.Elapsed + final.Elapsed));
            }
        }
        finally
        {
            started.Dispose();
            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseAppCountdownOnAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var started = await StartAppCountdownAsync(config, delaySeconds: 120, action: 1, cancellationToken).ConfigureAwait(false);
        try
        {
            var offElapsed = await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 0, cancellationToken).ConfigureAwait(false);
            try
            {
                var onRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-on",
                    true,
                    $"delay=120s, {SummarizeAppCountdownStart(started)}, manual Off write={offElapsed.TotalMilliseconds:0}ms, restored On after {onRead.Elapsed.TotalSeconds:0.0}s",
                    started.Elapsed + offElapsed + onRead.Elapsed));
            }
            catch (TimeoutException ex)
            {
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-on",
                    false,
                    $"delay=120s, {SummarizeAppCountdownStart(started)}, manual Off write={offElapsed.TotalMilliseconds:0}ms, did not fire: {ex.Message}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    started.Elapsed + offElapsed + final.Elapsed));
            }
        }
        finally
        {
            started.Dispose();
            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseAppCountdownOnFromOffAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var offElapsed = await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 0, cancellationToken).ConfigureAwait(false);
        var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        var started = await StartAppCountdownAsync(config, delaySeconds: 120, action: 1, cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var onRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-on-from-off",
                    true,
                    $"delay=120s, initial Off write={offElapsed.TotalMilliseconds:0}ms, Off readback={offRead.Elapsed.TotalMilliseconds:0}ms, {SummarizeAppCountdownStart(started)}, restored On after {onRead.Elapsed.TotalSeconds:0.0}s",
                    offElapsed + offRead.Elapsed + started.Elapsed + onRead.Elapsed));
            }
            catch (TimeoutException ex)
            {
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-on-from-off",
                    false,
                    $"delay=120s, initial Off write={offElapsed.TotalMilliseconds:0}ms, Off readback={offRead.Elapsed.TotalMilliseconds:0}ms, {SummarizeAppCountdownStart(started)}, did not fire: {ex.Message}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    offElapsed + offRead.Elapsed + started.Elapsed + final.Elapsed));
            }
        }
        finally
        {
            started.Dispose();
            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseSchedulePairFireAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var time = await SendCommandAsync(config, "{\"time\":{\"get_time\":{}}}", cancellationToken).ConfigureAwait(false);
        var deviceTime = TryGetDeviceTime(time.Response, out var parsedTime) ? parsedTime : DateTime.Now;
        time.Response.Dispose();
        var offTime = deviceTime.AddMinutes(2);
        var onTime = deviceTime.AddMinutes(3);
        var offMinute = offTime.Hour * 60 + offTime.Minute;
        var onMinute = onTime.Hour * 60 + onTime.Minute;
        var offWeekdays = BuildWeekdayMask(offTime.DayOfWeek);
        var onWeekdays = BuildWeekdayMask(onTime.DayOfWeek);
        var addOff = await SendCommandAsync(config, BuildScheduleRuleCommand("HVO Lab Off", offMinute, offWeekdays, 0), cancellationToken).ConfigureAwait(false);
        var addOn = await SendCommandAsync(config, BuildScheduleRuleCommand("HVO Lab On", onMinute, onWeekdays, 1), cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var offRuleId = GetRuleIdByName(readback.Response, "schedule", "HVO Lab Off");
        var onRuleId = GetRuleIdByName(readback.Response, "schedule", "HVO Lab On");
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(155), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var onRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var deleteOff = offRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", offRuleId), cancellationToken).ConfigureAwait(false);
                var deleteOn = onRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", onRuleId), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-pair-fire",
                    true,
                    $"off minute={offMinute}, on minute={onMinute}, add off err={GetErrCode(addOff.Response, "schedule", "add_rule")}, add on err={GetErrCode(addOn.Response, "schedule", "add_rule")}, off id={(offRuleId is null ? "no" : "yes")}, on id={(onRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "schedule")}, Off after {offRead.Elapsed.TotalSeconds:0.0}s, On after {onRead.Elapsed.TotalSeconds:0.0}s, delete off err={(deleteOff is null ? "skipped" : GetErrCode(deleteOff.Response, "schedule", "delete_rule"))}, delete on err={(deleteOn is null ? "skipped" : GetErrCode(deleteOn.Response, "schedule", "delete_rule"))}",
                    addOff.Elapsed + addOn.Elapsed + readback.Elapsed + offRead.Elapsed + onRead.Elapsed + (deleteOff?.Elapsed ?? TimeSpan.Zero) + (deleteOn?.Elapsed ?? TimeSpan.Zero)));
                deleteOff?.Response.Dispose();
                deleteOn?.Response.Dispose();
            }
            catch (TimeoutException ex)
            {
                var deleteOff = offRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", offRuleId), cancellationToken).ConfigureAwait(false);
                var deleteOn = onRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", onRuleId), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-pair-fire",
                    false,
                    $"off minute={offMinute}, on minute={onMinute}, add off err={GetErrCode(addOff.Response, "schedule", "add_rule")}, add on err={GetErrCode(addOn.Response, "schedule", "add_rule")}, off id={(offRuleId is null ? "no" : "yes")}, on id={(onRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "schedule")}, did not complete: {ex.Message}, delete off err={(deleteOff is null ? "skipped" : GetErrCode(deleteOff.Response, "schedule", "delete_rule"))}, delete on err={(deleteOn is null ? "skipped" : GetErrCode(deleteOn.Response, "schedule", "delete_rule"))}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    addOff.Elapsed + addOn.Elapsed + readback.Elapsed + (deleteOff?.Elapsed ?? TimeSpan.Zero) + (deleteOn?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                deleteOff?.Response.Dispose();
                deleteOn?.Response.Dispose();
            }
        }
        finally
        {
            addOff.Response.Dispose();
            addOn.Response.Dispose();
            readback.Response.Dispose();
            if (offRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", offRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            if (onRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", onRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseAppScheduleRoundtripAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        const string appWeekdays = "0,0,0,1,1,1,1";
        var before = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var offRuleId = GetRuleIdBySchedule(before.Response, startAction: 0, startMinute: 950, repeat: 1, weekdays: [0, 0, 0, 1, 1, 1, 1]);
        var onRuleId = GetRuleIdBySchedule(before.Response, startAction: 1, startMinute: 955, repeat: 1, weekdays: [0, 0, 0, 1, 1, 1, 1]);
        var deleteOff = offRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", offRuleId), cancellationToken).ConfigureAwait(false);
        var deleteOn = onRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", onRuleId), cancellationToken).ConfigureAwait(false);
        var afterDelete = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var addOff = await SendCommandAsync(config, BuildScheduleRuleCommand("name", 950, appWeekdays, 0, repeat: 1), cancellationToken).ConfigureAwait(false);
        var addOn = await SendCommandAsync(config, BuildScheduleRuleCommand("name", 955, appWeekdays, 1, repeat: 1), cancellationToken).ConfigureAwait(false);
        var afterAdd = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);

        steps.Add(new KasaPlugExerciseStep(
            "schedule-roundtrip",
            GetErrCode(addOff.Response, "schedule", "add_rule") == 0 && GetErrCode(addOn.Response, "schedule", "add_rule") == 0,
            $"before rules={GetRuleCount(before.Response, "schedule")}, matched off id={(offRuleId is null ? "no" : "yes")}, matched on id={(onRuleId is null ? "no" : "yes")}, delete off err={(deleteOff is null ? "skipped" : GetErrCode(deleteOff.Response, "schedule", "delete_rule"))}, delete on err={(deleteOn is null ? "skipped" : GetErrCode(deleteOn.Response, "schedule", "delete_rule"))}, after delete rules={GetRuleCount(afterDelete.Response, "schedule")}, add off err={GetErrCode(addOff.Response, "schedule", "add_rule")}, add on err={GetErrCode(addOn.Response, "schedule", "add_rule")}, after add rules={GetRuleCount(afterAdd.Response, "schedule")}",
            before.Elapsed + (deleteOff?.Elapsed ?? TimeSpan.Zero) + (deleteOn?.Elapsed ?? TimeSpan.Zero) + afterDelete.Elapsed + addOff.Elapsed + addOn.Elapsed + afterAdd.Elapsed));

        before.Response.Dispose();
        deleteOff?.Response.Dispose();
        deleteOn?.Response.Dispose();
        afterDelete.Response.Dispose();
        addOff.Response.Dispose();
        addOn.Response.Dispose();
        afterAdd.Response.Dispose();
    }

    private async Task ExerciseScheduleClearAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var before = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var ruleIds = GetRuleIds(before.Response, "schedule").ToArray();
        var deleteResults = new List<KasaPlugLabReadResponse>();
        try
        {
            foreach (var ruleId in ruleIds)
            {
                deleteResults.Add(await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", ruleId), cancellationToken).ConfigureAwait(false));
            }

            var after = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
            try
            {
                var deleteSummary = deleteResults.Count == 0
                    ? "none"
                    : string.Join(',', deleteResults.Select(result => GetErrCode(result.Response, "schedule", "delete_rule")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"));
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-clear",
                    GetRuleCount(after.Response, "schedule") == 0,
                    $"before rules={GetRuleCount(before.Response, "schedule")}, deleted={deleteResults.Count}, delete errs=[{deleteSummary}], after rules={GetRuleCount(after.Response, "schedule")}",
                    before.Elapsed + TimeSpan.FromTicks(deleteResults.Sum(result => result.Elapsed.Ticks)) + after.Elapsed));
            }
            finally
            {
                after.Response.Dispose();
            }
        }
        finally
        {
            before.Response.Dispose();
            foreach (var result in deleteResults)
            {
                result.Response.Dispose();
            }
        }
    }

    private async Task ExercisePrearmedPowerCycleAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var time = await SendCommandAsync(config, "{\"time\":{\"get_time\":{}}}", cancellationToken).ConfigureAwait(false);
        var deviceTime = TryGetDeviceTime(time.Response, out var parsedTime) ? parsedTime : DateTime.Now;
        time.Response.Dispose();
        var scheduledTime = deviceTime.AddMinutes(1);
        var scheduledMinute = scheduledTime.Hour * 60 + scheduledTime.Minute;
        var weekdays = BuildWeekdayMask(scheduledTime.DayOfWeek);
        var add = await SendCommandAsync(config, BuildScheduleRuleCommand("HVO Lab", scheduledMinute, weekdays, 1), cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var createdRuleId = GetRuleIdByName(readback.Response, "schedule", "HVO Lab");
        try
        {
            var offElapsed = await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 0, cancellationToken).ConfigureAwait(false);
            var onRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false);
            steps.Add(new KasaPlugExerciseStep(
                "prearmed-power-cycle",
                true,
                $"one-time schedule On minute={scheduledMinute}, add err={GetErrCode(add.Response, "schedule", "add_rule")}, created id={(createdRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "schedule")}, manual Off write={offElapsed.TotalMilliseconds:0}ms, restored On after {onRead.Elapsed.TotalSeconds:0.0}s, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "schedule", "delete_rule"))}",
                add.Elapsed + readback.Elapsed + offElapsed + onRead.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero)));
            delete?.Response.Dispose();
        }
        finally
        {
            add.Response.Dispose();
            readback.Response.Dispose();
            if (createdRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseAppOneTimeScheduleFireAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var time = await SendCommandAsync(config, "{\"time\":{\"get_time\":{}}}", cancellationToken).ConfigureAwait(false);
        var deviceTime = TryGetDeviceTime(time.Response, out var parsedTime) ? parsedTime : DateTime.Now;
        time.Response.Dispose();
        var scheduledTime = deviceTime.AddMinutes(1);
        var scheduledMinute = scheduledTime.Hour * 60 + scheduledTime.Minute;
        var weekdays = BuildWeekdayMask(scheduledTime.DayOfWeek);
        var add = await SendCommandAsync(config, BuildScheduleRuleCommand("name", scheduledMinute, weekdays, 0, repeat: 0), cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var next = await SendCommandAsync(config, "{\"schedule\":{\"get_next_action\":{}}}", cancellationToken).ConfigureAwait(false);
        var createdRuleId = GetRuleIdBySchedule(readback.Response, startAction: 0, startMinute: scheduledMinute, repeat: 0, BuildWeekdayArray(scheduledTime.DayOfWeek));
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-one-time",
                    GetErrCode(add.Response, "schedule", "add_rule") == 0 && createdRuleId is not null,
                    $"target minute={scheduledMinute}, add err={GetErrCode(add.Response, "schedule", "add_rule")}, created id={(createdRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "schedule")}, next: {SummarizeNextAction(next.Response, "schedule")}, fired Off after {offRead.Elapsed.TotalSeconds:0.0}s, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "schedule", "delete_rule"))}",
                    add.Elapsed + readback.Elapsed + next.Elapsed + offRead.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero)));
                delete?.Response.Dispose();
            }
            catch (TimeoutException ex)
            {
                var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-one-time",
                    false,
                    $"target minute={scheduledMinute}, add err={GetErrCode(add.Response, "schedule", "add_rule")}, created id={(createdRuleId is null ? "no" : "yes")}, rules after add={GetRuleCount(readback.Response, "schedule")}, next: {SummarizeNextAction(next.Response, "schedule")}, did not fire: {ex.Message}, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "schedule", "delete_rule"))}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    add.Elapsed + readback.Elapsed + next.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                delete?.Response.Dispose();
            }
        }
        finally
        {
            add.Response.Dispose();
            readback.Response.Dispose();
            next.Response.Dispose();
            if (createdRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", createdRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseScheduleRebootPersistenceAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var time = await SendCommandAsync(config, "{\"time\":{\"get_time\":{}}}", cancellationToken).ConfigureAwait(false);
        var deviceTime = TryGetDeviceTime(time.Response, out var parsedTime) ? parsedTime : DateTime.Now;
        time.Response.Dispose();
        var offTime = deviceTime.AddMinutes(3);
        var onTime = deviceTime.AddMinutes(4);
        var offMinute = offTime.Hour * 60 + offTime.Minute;
        var onMinute = onTime.Hour * 60 + onTime.Minute;
        var offWeekdays = BuildWeekdayMask(offTime.DayOfWeek);
        var onWeekdays = BuildWeekdayMask(onTime.DayOfWeek);
        var addOff = await SendCommandAsync(config, BuildScheduleRuleCommand("name", offMinute, offWeekdays, 0, repeat: 1), cancellationToken).ConfigureAwait(false);
        var addOn = await SendCommandAsync(config, BuildScheduleRuleCommand("name", onMinute, onWeekdays, 1, repeat: 1), cancellationToken).ConfigureAwait(false);
        var beforeReboot = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var offRuleId = GetRuleIdBySchedule(beforeReboot.Response, startAction: 0, startMinute: offMinute, repeat: 1, BuildWeekdayArray(offTime.DayOfWeek));
        var onRuleId = GetRuleIdBySchedule(beforeReboot.Response, startAction: 1, startMinute: onMinute, repeat: 1, BuildWeekdayArray(onTime.DayOfWeek));
        var rebooted = await RebootAndWaitAsync(config, cancellationToken).ConfigureAwait(false);
        var afterReboot = await SendCommandAsync(config, "{\"schedule\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var next = await SendCommandAsync(config, "{\"schedule\":{\"get_next_action\":{}}}", cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(245), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var onRead = await WaitForRelayStateAsync(config, 1, TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var deleteOff = offRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", offRuleId), cancellationToken).ConfigureAwait(false);
                var deleteOn = onRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", onRuleId), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-reboot",
                    true,
                    $"repeat=1, off minute={offMinute}, on minute={onMinute}, add off err={GetErrCode(addOff.Response, "schedule", "add_rule")}, add on err={GetErrCode(addOn.Response, "schedule", "add_rule")}, ids off/on={(offRuleId is null ? "no" : "yes")}/{(onRuleId is null ? "no" : "yes")}, before reboot={SummarizeRules(beforeReboot.Response, "schedule")}, reboot err={GetErrCode(rebooted.Reboot.Response, "system", "reboot")}, tcp drop={(rebooted.SawOffline ? "yes" : "no")}, online={rebooted.Elapsed.TotalSeconds:0.0}s, after reboot={SummarizeRules(afterReboot.Response, "schedule")}, next: {SummarizeNextAction(next.Response, "schedule")}, Off after {offRead.Elapsed.TotalSeconds:0.0}s, On after {onRead.Elapsed.TotalSeconds:0.0}s, delete errs={(deleteOff is null ? "skipped" : GetErrCode(deleteOff.Response, "schedule", "delete_rule"))}/{(deleteOn is null ? "skipped" : GetErrCode(deleteOn.Response, "schedule", "delete_rule"))}",
                    addOff.Elapsed + addOn.Elapsed + beforeReboot.Elapsed + rebooted.Reboot.Elapsed + rebooted.Elapsed + afterReboot.Elapsed + next.Elapsed + offRead.Elapsed + onRead.Elapsed + (deleteOff?.Elapsed ?? TimeSpan.Zero) + (deleteOn?.Elapsed ?? TimeSpan.Zero)));
                deleteOff?.Response.Dispose();
                deleteOn?.Response.Dispose();
            }
            catch (TimeoutException ex)
            {
                var deleteOff = offRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", offRuleId), cancellationToken).ConfigureAwait(false);
                var deleteOn = onRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", onRuleId), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "schedule-reboot",
                    false,
                    $"repeat=1, off minute={offMinute}, on minute={onMinute}, add off err={GetErrCode(addOff.Response, "schedule", "add_rule")}, add on err={GetErrCode(addOn.Response, "schedule", "add_rule")}, ids off/on={(offRuleId is null ? "no" : "yes")}/{(onRuleId is null ? "no" : "yes")}, before reboot={SummarizeRules(beforeReboot.Response, "schedule")}, reboot err={GetErrCode(rebooted.Reboot.Response, "system", "reboot")}, tcp drop={(rebooted.SawOffline ? "yes" : "no")}, online={rebooted.Elapsed.TotalSeconds:0.0}s, after reboot={SummarizeRules(afterReboot.Response, "schedule")}, next: {SummarizeNextAction(next.Response, "schedule")}, did not complete: {ex.Message}, delete errs={(deleteOff is null ? "skipped" : GetErrCode(deleteOff.Response, "schedule", "delete_rule"))}/{(deleteOn is null ? "skipped" : GetErrCode(deleteOn.Response, "schedule", "delete_rule"))}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    addOff.Elapsed + addOn.Elapsed + beforeReboot.Elapsed + rebooted.Reboot.Elapsed + rebooted.Elapsed + afterReboot.Elapsed + next.Elapsed + (deleteOff?.Elapsed ?? TimeSpan.Zero) + (deleteOn?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                deleteOff?.Response.Dispose();
                deleteOn?.Response.Dispose();
            }
        }
        finally
        {
            addOff.Response.Dispose();
            addOn.Response.Dispose();
            beforeReboot.Response.Dispose();
            rebooted.Reboot.Response.Dispose();
            afterReboot.Response.Dispose();
            next.Response.Dispose();
            if (offRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", offRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            if (onRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("schedule", onRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExerciseAwayRuleRoundtripAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var time = await SendCommandAsync(config, "{\"time\":{\"get_time\":{}}}", cancellationToken).ConfigureAwait(false);
        var deviceTime = TryGetDeviceTime(time.Response, out var parsedTime) ? parsedTime : DateTime.Now;
        time.Response.Dispose();
        var startTime = deviceTime.AddMinutes(2);
        var startMinute = startTime.Hour * 60 + startTime.Minute;
        var weekdays = BuildWeekdayMask(startTime.DayOfWeek);
        var before = await SendCommandAsync(config, "{\"anti_theft\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var add = await SendCommandAsync(config, BuildRuleCommand("anti_theft", "HVO Lab", startMinute, weekdays, 1, repeat: 0), cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"anti_theft\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var next = await SendCommandAsync(config, "{\"anti_theft\":{\"get_next_action\":{}}}", cancellationToken).ConfigureAwait(false);
        var createdRuleId = GetRuleIdByName(readback.Response, "anti_theft", "HVO Lab");
        var delete = createdRuleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("anti_theft", createdRuleId), cancellationToken).ConfigureAwait(false);
        var afterDelete = await SendCommandAsync(config, "{\"anti_theft\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        try
        {
            steps.Add(new KasaPlugExerciseStep(
                "away-roundtrip",
                GetErrCode(add.Response, "anti_theft", "add_rule") == 0 && (delete is null || GetErrCode(delete.Response, "anti_theft", "delete_rule") == 0),
                $"start minute={startMinute}, before={SummarizeRules(before.Response, "anti_theft")}, add err={GetErrCode(add.Response, "anti_theft", "add_rule")}, readback={SummarizeRules(readback.Response, "anti_theft")}, next: {SummarizeNextAction(next.Response, "anti_theft")}, created id={(createdRuleId is null ? "no" : "yes")}, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "anti_theft", "delete_rule"))}, after delete={SummarizeRules(afterDelete.Response, "anti_theft")}",
                before.Elapsed + add.Elapsed + readback.Elapsed + next.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero) + afterDelete.Elapsed));
        }
        finally
        {
            before.Response.Dispose();
            add.Response.Dispose();
            readback.Response.Dispose();
            next.Response.Dispose();
            delete?.Response.Dispose();
            afterDelete.Response.Dispose();
            if (createdRuleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("anti_theft", createdRuleId), cancellationToken).ConfigureAwait(false)).Response;
            }
        }
    }

    private async Task ExerciseCountdownRebootPersistenceAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await ExerciseHvoCountdownRebootPersistenceAsync(config, steps, cancellationToken).ConfigureAwait(false);
        await ExerciseAppCountdownRebootPersistenceAsync(config, steps, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExerciseHvoCountdownRebootPersistenceAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var add = await SendCommandAsync(config, "{\"count_down\":{\"add_rule\":{\"name\":\"HVO Lab\",\"enable\":1,\"delay\":180,\"act\":0}}}", cancellationToken).ConfigureAwait(false);
        var beforeReboot = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var ruleId = GetRuleIdByName(beforeReboot.Response, "count_down", "HVO Lab");
        var rebooted = await RebootAndWaitAsync(config, cancellationToken).ConfigureAwait(false);
        var afterReboot = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var scheduleNext = await SendCommandAsync(config, "{\"schedule\":{\"get_next_action\":{}}}", cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(220), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var delete = ruleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", ruleId), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "countdown-reboot",
                    true,
                    $"delay=180s, add err={GetErrCode(add.Response, "count_down", "add_rule")}, id={(ruleId is null ? "no" : "yes")}, before reboot={SummarizeRules(beforeReboot.Response, "count_down")}, reboot err={GetErrCode(rebooted.Reboot.Response, "system", "reboot")}, tcp drop={(rebooted.SawOffline ? "yes" : "no")}, online={rebooted.Elapsed.TotalSeconds:0.0}s, after reboot={SummarizeRules(afterReboot.Response, "count_down")}, schedule next: {SummarizeNextAction(scheduleNext.Response, "schedule")}, fired Off after {offRead.Elapsed.TotalSeconds:0.0}s, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "count_down", "delete_rule"))}",
                    add.Elapsed + beforeReboot.Elapsed + rebooted.Reboot.Elapsed + rebooted.Elapsed + afterReboot.Elapsed + scheduleNext.Elapsed + offRead.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero)));
                delete?.Response.Dispose();
            }
            catch (TimeoutException ex)
            {
                var delete = ruleId is null ? null : await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", ruleId), cancellationToken).ConfigureAwait(false);
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "countdown-reboot",
                    false,
                    $"delay=180s, add err={GetErrCode(add.Response, "count_down", "add_rule")}, id={(ruleId is null ? "no" : "yes")}, before reboot={SummarizeRules(beforeReboot.Response, "count_down")}, reboot err={GetErrCode(rebooted.Reboot.Response, "system", "reboot")}, tcp drop={(rebooted.SawOffline ? "yes" : "no")}, online={rebooted.Elapsed.TotalSeconds:0.0}s, after reboot={SummarizeRules(afterReboot.Response, "count_down")}, schedule next: {SummarizeNextAction(scheduleNext.Response, "schedule")}, did not fire: {ex.Message}, delete err={(delete is null ? "skipped" : GetErrCode(delete.Response, "count_down", "delete_rule"))}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    add.Elapsed + beforeReboot.Elapsed + rebooted.Reboot.Elapsed + rebooted.Elapsed + afterReboot.Elapsed + scheduleNext.Elapsed + (delete?.Elapsed ?? TimeSpan.Zero) + final.Elapsed));
                delete?.Response.Dispose();
            }
        }
        finally
        {
            add.Response.Dispose();
            beforeReboot.Response.Dispose();
            rebooted.Reboot.Response.Dispose();
            afterReboot.Response.Dispose();
            scheduleNext.Response.Dispose();
            if (ruleId is not null)
            {
                using var _ = (await SendCommandAsync(config, BuildDeleteRuleCommand("count_down", ruleId), cancellationToken).ConfigureAwait(false)).Response;
            }

            await RestoreRelayOnForCleanupAsync(config).ConfigureAwait(false);
        }
    }

    private async Task ExerciseAppCountdownRebootPersistenceAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var started = await StartAppCountdownAsync(config, delaySeconds: 180, action: 0, cancellationToken).ConfigureAwait(false);
        var rebooted = await RebootAndWaitAsync(config, cancellationToken).ConfigureAwait(false);
        var afterReboot = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var scheduleNext = await SendCommandAsync(config, "{\"schedule\":{\"get_next_action\":{}}}", cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var offRead = await WaitForRelayStateAsync(config, 0, TimeSpan.FromSeconds(220), TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-reboot",
                    true,
                    $"delay=180s, {SummarizeAppCountdownStart(started)}, reboot err={GetErrCode(rebooted.Reboot.Response, "system", "reboot")}, tcp drop={(rebooted.SawOffline ? "yes" : "no")}, online={rebooted.Elapsed.TotalSeconds:0.0}s, after reboot={SummarizeRules(afterReboot.Response, "count_down")}, schedule next: {SummarizeNextAction(scheduleNext.Response, "schedule")}, fired Off after {offRead.Elapsed.TotalSeconds:0.0}s",
                    started.Elapsed + rebooted.Reboot.Elapsed + rebooted.Elapsed + afterReboot.Elapsed + scheduleNext.Elapsed + offRead.Elapsed));
            }
            catch (TimeoutException ex)
            {
                var final = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                steps.Add(new KasaPlugExerciseStep(
                    "app-countdown-reboot",
                    false,
                    $"delay=180s, {SummarizeAppCountdownStart(started)}, reboot err={GetErrCode(rebooted.Reboot.Response, "system", "reboot")}, tcp drop={(rebooted.SawOffline ? "yes" : "no")}, online={rebooted.Elapsed.TotalSeconds:0.0}s, after reboot={SummarizeRules(afterReboot.Response, "count_down")}, schedule next: {SummarizeNextAction(scheduleNext.Response, "schedule")}, did not fire: {ex.Message}, final relay={(final.Info.RelayState == 1 ? "On" : "Off")}",
                    started.Elapsed + rebooted.Reboot.Elapsed + rebooted.Elapsed + afterReboot.Elapsed + scheduleNext.Elapsed + final.Elapsed));
            }
        }
        finally
        {
            started.Dispose();
            rebooted.Reboot.Response.Dispose();
            afterReboot.Response.Dispose();
            scheduleNext.Response.Dispose();
            await RestoreRelayOnForCleanupAsync(config).ConfigureAwait(false);
        }
    }

    private async Task ExerciseDiagnosticsAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var icon = await SendCommandAsync(config, "{\"system\":{\"get_dev_icon\":{}}}", cancellationToken).ConfigureAwait(false);
        var download = await SendCommandAsync(config, "{\"system\":{\"get_download_state\":{}}}", cancellationToken).ConfigureAwait(false);
        var cloud = await SendCommandAsync(config, "{\"cnCloud\":{\"get_info\":{}}}", cancellationToken).ConfigureAwait(false);
        var firmware = await SendCommandAsync(config, "{\"cnCloud\":{\"get_intl_fw_list\":{}}}", cancellationToken).ConfigureAwait(false);
        try
        {
            steps.Add(KasaPlugExerciseStep.Ok(
                "diagnostics",
                $"dev_icon err={GetErrCode(icon.Response, "system", "get_dev_icon")}, keys={GetPayloadKeys(icon.Response, "system", "get_dev_icon")}; download err={GetErrCode(download.Response, "system", "get_download_state")}, keys={GetPayloadKeys(download.Response, "system", "get_download_state")}; cloud err={GetErrCode(cloud.Response, "cnCloud", "get_info")}, keys={GetPayloadKeys(cloud.Response, "cnCloud", "get_info")}; fw err={GetErrCode(firmware.Response, "cnCloud", "get_intl_fw_list")}, keys={GetPayloadKeys(firmware.Response, "cnCloud", "get_intl_fw_list")}",
                icon.Elapsed + download.Elapsed + cloud.Elapsed + firmware.Elapsed));
        }
        finally
        {
            icon.Response.Dispose();
            download.Response.Dispose();
            cloud.Response.Dispose();
            firmware.Response.Dispose();
        }
    }

    private async Task InspectRulesAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        foreach (var module in new[] { "schedule", "count_down", "anti_theft" })
        {
            var rules = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"{module}\":{{\"get_rules\":{{}}}}}}"), cancellationToken).ConfigureAwait(false);
            var next = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"{module}\":{{\"get_next_action\":{{}}}}}}"), cancellationToken).ConfigureAwait(false);
            steps.Add(KasaPlugExerciseStep.Ok($"inspect-{module}", $"rules: {SummarizeRules(rules.Response, module)}; next: {SummarizeNextAction(next.Response, module)}", rules.Elapsed + next.Elapsed));
            rules.Response.Dispose();
            next.Response.Dispose();
        }
    }

    private async Task InspectRuntimeAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var daystat = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"schedule\":{{\"get_daystat\":{{\"year\":{now.Year},\"month\":{now.Month}}}}}}}"), cancellationToken).ConfigureAwait(false);
        var monthstat = await SendCommandAsync(config, string.Create(CultureInfo.InvariantCulture, $"{{\"schedule\":{{\"get_monthstat\":{{\"year\":{now.Year}}}}}}}"), cancellationToken).ConfigureAwait(false);
        steps.Add(KasaPlugExerciseStep.Ok("runtime", $"daystat: {SummarizeDayStats(daystat.Response, now)}; monthstat: {SummarizeMonthStats(monthstat.Response, now)}", daystat.Elapsed + monthstat.Elapsed));
        daystat.Response.Dispose();
        monthstat.Response.Dispose();
    }

    private async Task ExerciseRebootAsync(KasaDeviceConfig config, List<KasaPlugExerciseStep> steps, CancellationToken cancellationToken)
    {
        await SendRelayStateAsync(config.Host, config.EffectivePort(9999), 1, cancellationToken).ConfigureAwait(false);
        var rebooted = await RebootAndWaitAsync(config, cancellationToken).ConfigureAwait(false);

        steps.Add(new KasaPlugExerciseStep(
            "reboot",
            rebooted.Online is not null,
            $"command err={GetErrCode(rebooted.Reboot.Response, "system", "reboot")}, before relay={(rebooted.Before.Info.RelayState == 1 ? "On" : "Off")}, tcp drop={(rebooted.SawOffline ? "yes" : "no")}, first drop={rebooted.FirstOffline?.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) ?? "n/a"}s, back online={rebooted.Elapsed.TotalSeconds:0.0}s, after relay={(rebooted.Online?.Info.RelayState == 1 ? "On" : "Off")}, rssi={rebooted.Online?.Info.Rssi?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            rebooted.Reboot.Elapsed + rebooted.Elapsed));
        rebooted.Reboot.Response.Dispose();
    }

    private async Task<(KasaPlugLabRead Before, KasaPlugLabReadResponse Reboot, bool SawOffline, TimeSpan? FirstOffline, KasaPlugLabRead? Online, TimeSpan Elapsed)> RebootAndWaitAsync(KasaDeviceConfig config, CancellationToken cancellationToken)
    {
        var before = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
        var reboot = await SendCommandAsync(config, "{\"system\":{\"reboot\":{\"delay\":1}}}", cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        var sawOffline = false;
        TimeSpan? firstOffline = null;
        KasaPlugLabRead? online = null;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(90))
        {
            try
            {
                online = await ReadValidatedAsync(config, cancellationToken).ConfigureAwait(false);
                if (sawOffline)
                {
                    break;
                }
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException)
            {
                sawOffline = true;
                firstOffline ??= stopwatch.Elapsed;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return (before, reboot, sawOffline, firstOffline, online, stopwatch.Elapsed);
    }

    private async Task<KasaPlugLabReadResponse> SendCommandAsync(KasaDeviceConfig config, string command, CancellationToken cancellationToken) =>
        await SendAsync(config.Host, config.EffectivePort(9999), command, cancellationToken).ConfigureAwait(false);

    private static string BuildAliasCommand(string alias) =>
        $"{{\"system\":{{\"set_dev_alias\":{{\"alias\":{JsonSerializer.Serialize(alias)}}}}}}}";

    private static string BuildSetBrightnessCommand(int brightness) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"smartlife.iot.dimmer\":{{\"set_brightness\":{{\"brightness\":{brightness}}}}}}}");

    private static string BuildSetTimeCommand(DateTime time) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"time\":{{\"set_time\":{{\"year\":{time.Year},\"month\":{time.Month},\"mday\":{time.Day},\"hour\":{time.Hour},\"min\":{time.Minute},\"sec\":{time.Second}}}}}}}");

    private static string BuildSetTimezoneCommand(DateTime time, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"time\":{{\"set_timezone\":{{\"year\":{time.Year},\"month\":{time.Month},\"mday\":{time.Day},\"hour\":{time.Hour},\"min\":{time.Minute},\"sec\":{time.Second},\"index\":{index}}}}}}}");

    private static string BuildScheduleRuleCommand(int startMinute, string weekdays, int startAction) =>
        BuildScheduleRuleCommand("HVO Lab", startMinute, weekdays, startAction);

    private static string BuildScheduleRuleCommand(string name, int startMinute, string weekdays, int startAction) =>
        BuildScheduleRuleCommand(name, startMinute, weekdays, startAction, repeat: 0);

    private static string BuildScheduleRuleCommand(string name, int startMinute, string weekdays, int startAction, int repeat) =>
        BuildRuleCommand("schedule", name, startMinute, weekdays, startAction, repeat);

    private static string BuildRuleCommand(string module, string name, int startMinute, string weekdays, int startAction, int repeat) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"{module}\":{{\"add_rule\":{{\"name\":{JsonSerializer.Serialize(name)},\"enable\":1,\"wday\":[{weekdays}],\"repeat\":{repeat},\"sact\":{startAction},\"stime_opt\":0,\"smin\":{startMinute},\"soffset\":0,\"eact\":-1,\"etime_opt\":-1,\"emin\":0}}}}}}");

    private static string BuildWeekdayMask(DayOfWeek dayOfWeek)
    {
        var days = new int[7];
        days[(int)dayOfWeek] = 1;
        return string.Join(',', days);
    }

    private static int[] BuildWeekdayArray(DayOfWeek dayOfWeek)
    {
        var days = new int[7];
        days[(int)dayOfWeek] = 1;
        return days;
    }

    private static string BuildDeleteRuleCommand(string module, string ruleId) =>
        $"{{\"{module}\":{{\"delete_rule\":{{\"id\":{JsonSerializer.Serialize(ruleId)}}}}}}}";

    private static string BuildAppCountdownAddRuleCommand(int delaySeconds, int action) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"count_down\":{{\"add_rule\":{{\"name\":\"Timer AddTimerObject\",\"enable\":1,\"delay\":{delaySeconds},\"act\":{action}}}}}}}");

    private static string BuildAppCountdownEditRuleCommand(string ruleId, int delaySeconds, int action) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"count_down\":{{\"edit_rule\":{{\"id\":{JsonSerializer.Serialize(ruleId)},\"name\":\"Timer AddTimerObject\",\"enable\":1,\"delay\":{delaySeconds},\"act\":{action}}}}}}}");

    private async Task<AppCountdownStart> StartAppCountdownAsync(KasaDeviceConfig config, int delaySeconds, int action, CancellationToken cancellationToken)
    {
        var before = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var existingRuleId = GetRuleIdByName(before.Response, "count_down", "Timer AddTimerObject");
        var commandName = existingRuleId is null ? "add_rule" : "edit_rule";
        var command = existingRuleId is null
            ? BuildAppCountdownAddRuleCommand(delaySeconds, action)
            : BuildAppCountdownEditRuleCommand(existingRuleId, delaySeconds, action);
        var start = await SendCommandAsync(config, command, cancellationToken).ConfigureAwait(false);
        var readback = await SendCommandAsync(config, "{\"count_down\":{\"get_rules\":{}}}", cancellationToken).ConfigureAwait(false);
        var scheduleNext = await SendCommandAsync(config, "{\"schedule\":{\"get_next_action\":{}}}", cancellationToken).ConfigureAwait(false);
        return new AppCountdownStart(before, start, readback, scheduleNext, commandName, existingRuleId, delaySeconds, action);
    }

    private static string SummarizeAppCountdownStart(AppCountdownStart started) =>
        $"command={started.CommandName}, existing id={(started.ExistingRuleId is null ? "no" : "yes")}, err={GetErrCode(started.Start.Response, "count_down", started.CommandName)}, before={SummarizeRules(started.Before.Response, "count_down")}, after={SummarizeRules(started.Readback.Response, "count_down")}, schedule next: {SummarizeNextAction(started.ScheduleNext.Response, "schedule")}";

    private static int? GetErrCode(JsonDocument response, string module, string command) =>
        GetInt(response, module, command, "err_code");

    private static int? GetRuleCount(JsonDocument response, string module)
    {
        if (TryGetNested(response.RootElement, [module, "get_rules", "rule_list"], out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            return rules.GetArrayLength();
        }

        return null;
    }

    private static string? GetRuleIdByName(JsonDocument response, string module, string name)
    {
        if (!TryGetNested(response.RootElement, [module, "get_rules", "rule_list"], out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.Object
                && rule.TryGetProperty("name", out var ruleName)
                && ruleName.ValueKind == JsonValueKind.String
                && string.Equals(ruleName.GetString(), name, StringComparison.Ordinal)
                && rule.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }

        return null;
    }

    private static string? GetRuleIdBySchedule(JsonDocument response, int startAction, int startMinute, int repeat, int[] weekdays)
    {
        if (!TryGetNested(response.RootElement, ["schedule", "get_rules", "rule_list"], out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.Object
                && GetInt(rule, "sact") == startAction
                && GetInt(rule, "smin") == startMinute
                && GetInt(rule, "repeat") == repeat
                && GetInt(rule, "enable") == 1
                && GetInt(rule, "stime_opt") == 0
                && GetInt(rule, "soffset") == 0
                && GetInt(rule, "eact") == -1
                && GetInt(rule, "etime_opt") == -1
                && GetInt(rule, "emin") == 0
                && HasWeekdayMask(rule, weekdays)
                && rule.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } idText)
            {
                return idText;
            }
        }

        return null;
    }

    private static bool HasWeekdayMask(JsonElement rule, int[] weekdays)
    {
        if (!rule.TryGetProperty("wday", out var wday) || wday.ValueKind != JsonValueKind.Array || wday.GetArrayLength() != weekdays.Length)
        {
            return false;
        }

        var index = 0;
        foreach (var day in wday.EnumerateArray())
        {
            if (day.ValueKind != JsonValueKind.Number || !day.TryGetInt32(out var dayValue) || dayValue != weekdays[index])
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static string? GetNewRuleIdByName(JsonDocument before, JsonDocument after, string module, string name)
    {
        var existingIds = GetRuleIdsByName(before, module, name).ToHashSet(StringComparer.Ordinal);
        foreach (var id in GetRuleIdsByName(after, module, name))
        {
            if (!existingIds.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetRuleIdsByName(JsonDocument response, string module, string name)
    {
        if (!TryGetNested(response.RootElement, [module, "get_rules", "rule_list"], out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.Object
                && rule.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && string.Equals(nameElement.GetString(), name, StringComparison.Ordinal)
                && rule.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } idText)
            {
                yield return idText;
            }
        }
    }

    private static IEnumerable<string> GetRuleIds(JsonDocument response, string module)
    {
        if (!TryGetNested(response.RootElement, [module, "get_rules", "rule_list"], out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.Object
                && rule.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } idText)
            {
                yield return idText;
            }
        }
    }

    private static string SummarizeRules(JsonDocument response, string module)
    {
        var err = GetErrCode(response, module, "get_rules");
        if (!TryGetNested(response.RootElement, [module, "get_rules", "rule_list"], out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return $"err={err}, rules=unknown";
        }

        var summaries = new List<string>();
        foreach (var rule in rules.EnumerateArray())
        {
            summaries.Add(SummarizeRule(rule));
        }

        return $"err={err}, enable={GetInt(response, module, "get_rules", "enable")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, version={GetInt(response, module, "get_rules", "version")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, count={rules.GetArrayLength()}, [{string.Join("; ", summaries)}]";
    }

    private static string SummarizeRule(JsonElement rule)
    {
        if (rule.ValueKind != JsonValueKind.Object)
        {
            return "non-object";
        }

        return string.Join(", ", new[]
        {
            $"id={(rule.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? "present" : "missing")}",
            $"name={GetString(rule, "name") ?? "unknown"}",
            $"enable={GetInt(rule, "enable")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"repeat={GetInt(rule, "repeat")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"wday={GetArrayText(rule, "wday")}",
            $"sact={GetInt(rule, "sact")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"stime_opt={GetInt(rule, "stime_opt")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"smin={GetInt(rule, "smin")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"soffset={GetInt(rule, "soffset")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"eact={GetInt(rule, "eact")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"etime_opt={GetInt(rule, "etime_opt")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"emin={GetInt(rule, "emin")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"delay={GetInt(rule, "delay")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"remain={GetInt(rule, "remain")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"act={GetInt(rule, "act")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}"
        });
    }

    private static string SummarizeNextAction(JsonDocument response, string module) =>
        $"err={GetErrCode(response, module, "get_next_action")}, type={GetInt(response, module, "get_next_action", "type")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, action={GetInt(response, module, "get_next_action", "action")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, schd_sec={GetInt(response, module, "get_next_action", "schd_sec")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, schd_time={GetInt(response, module, "get_next_action", "schd_time")?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";

    private static string GetPayloadKeys(JsonDocument response, string module, string command)
    {
        if (!TryGetNested(response.RootElement, [module, command], out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return "unknown";
        }

        return $"[{string.Join(',', payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal))}]";
    }

    private static string SummarizeDayStats(JsonDocument response, DateTime now)
    {
        var err = GetErrCode(response, "schedule", "get_daystat");
        if (!TryGetNested(response.RootElement, ["schedule", "get_daystat", "day_list"], out var days) || days.ValueKind != JsonValueKind.Array)
        {
            return $"err={err}, days=unknown";
        }

        var entries = days.EnumerateArray()
            .Select(day => new { Day = GetInt(day, "day"), Time = GetInt(day, "time") })
            .Where(day => day.Day is not null && day.Time is not null)
            .ToArray();
        var today = entries.LastOrDefault(day => day.Day == now.Day)?.Time;
        var last7 = entries.Where(day => day.Day >= Math.Max(1, now.Day - 6) && day.Day <= now.Day).Sum(day => day.Time ?? 0);
        var last30 = entries.Where(day => day.Day >= Math.Max(1, now.Day - 29) && day.Day <= now.Day).Sum(day => day.Time ?? 0);
        return $"err={err}, count={days.GetArrayLength()}, today_min={today?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, last7_min={last7}, last30_current_month_min={last30}";
    }

    private static string SummarizeMonthStats(JsonDocument response, DateTime now)
    {
        var err = GetErrCode(response, "schedule", "get_monthstat");
        if (!TryGetNested(response.RootElement, ["schedule", "get_monthstat", "month_list"], out var months) || months.ValueKind != JsonValueKind.Array)
        {
            return $"err={err}, months=unknown";
        }

        var thisMonth = months.EnumerateArray()
            .Select(month => new { Month = GetInt(month, "month"), Time = GetInt(month, "time") })
            .LastOrDefault(month => month.Month == now.Month)?.Time;
        return $"err={err}, count={months.GetArrayLength()}, this_month_min={thisMonth?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
    }

    private static int? GetInt(JsonDocument response, string module, string command, string property)
    {
        if (TryGetNested(response.RootElement, [module, command, property], out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
        {
            return result;
        }

        return null;
    }

    private static bool TryGetDeviceTime(JsonDocument response, out DateTime time)
    {
        time = default;
        if (!TryGetNested(response.RootElement, ["time", "get_time"], out var value))
        {
            return false;
        }

        if (!TryGetInt(value, "year", out var year)
            || !TryGetInt(value, "month", out var month)
            || !TryGetInt(value, "mday", out var day)
            || !TryGetInt(value, "hour", out var hour)
            || !TryGetInt(value, "min", out var minute)
            || !TryGetInt(value, "sec", out var second))
        {
            return false;
        }

        time = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return true;
    }

    private static bool TryGetNested(JsonElement root, string[] path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static int? GetInt(JsonElement element, string name) =>
        TryGetInt(element, name, out var value) ? value : null;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static string GetArrayText(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return "unknown";
        }

        return $"[{string.Join(',', property.EnumerateArray().Select(item => item.ToString()))}]";
    }
}

public sealed record KasaPlugLabOptions(
    string Host,
    int Port,
    string DeviceId,
    string? SourceId,
    string? MacAddress,
    string ExpectedModel,
    string Target,
    TimeSpan ReadbackTimeout,
    TimeSpan ReadbackPollInterval);

internal sealed record AppCountdownStart(
    KasaPlugLabReadResponse Before,
    KasaPlugLabReadResponse Start,
    KasaPlugLabReadResponse Readback,
    KasaPlugLabReadResponse ScheduleNext,
    string CommandName,
    string? ExistingRuleId,
    int DelaySeconds,
    int Action) : IDisposable
{
    public TimeSpan Elapsed => Before.Elapsed + Start.Elapsed + Readback.Elapsed + ScheduleNext.Elapsed;

    public void Dispose()
    {
        Before.Response.Dispose();
        Start.Response.Dispose();
        Readback.Response.Dispose();
        ScheduleNext.Response.Dispose();
    }
}

public sealed record KasaPlugLabRun(
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    bool StartedOn,
    IReadOnlyList<KasaPlugLabObservation> Observations);

public sealed record KasaPlugLabObservation(string Step, TimeSpan? WriteElapsed, bool? IsOn, TimeSpan ReadbackElapsed);

public sealed record KasaPlugExerciseRun(
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    string OriginalAlias,
    string? FinalAlias,
    bool FinalOn,
    IReadOnlyList<KasaPlugExerciseStep> Steps);

public sealed record KasaPlugExerciseStep(string Step, bool Success, string Detail, TimeSpan Elapsed)
{
    public static KasaPlugExerciseStep Ok(string step, string detail, TimeSpan elapsed) => new(step, true, detail, elapsed);

    public static KasaPlugExerciseStep Skipped(string step, string detail, TimeSpan elapsed) => new(step, false, detail, elapsed);
}

internal sealed record KasaPlugLabRead(KasaSystemInfo Info, TimeSpan Elapsed);

internal sealed record KasaPlugLabReadResponse(JsonDocument Response, TimeSpan Elapsed);