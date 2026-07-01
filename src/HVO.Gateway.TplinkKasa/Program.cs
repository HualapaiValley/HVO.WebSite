using HVO.Edge.Outbox;
using HVO.Edge.Contracts;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Components;
using HVO.Gateway.TplinkKasa.Hosting;
using HVO.Gateway.TplinkKasa.Outbox;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Telemetry;
using HVO.Gateway.TplinkKasa.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var command = args.FirstOrDefault()?.ToLowerInvariant();
if (command is "probe" or "scan" or "plug-lab")
{
    return await RunCliAsync(command, args.Skip(1).ToArray()).ConfigureAwait(false);
}

if (command is not null && !command.StartsWith("--", StringComparison.Ordinal))
{
    PrintUsage();
    return 2;
}

await RunGatewayAsync(args).ConfigureAwait(false);
return 0;

static async Task<int> RunCliAsync(string command, string[] args)
{
    var options = ParseOptions(args);
    var port = GetIntOption(options, "port", 9999);
    var timeoutSeconds = GetIntOption(options, "timeout", 3);
    var includeIdentifiers = GetBoolOption(options, "include-identifiers", false);
    var includeLocators = GetBoolOption(options, "include-locators", false);
    var includePrivacySensitive = GetBoolOption(options, "include-privacy-sensitive", false);
    var summary = GetBoolOption(options, "summary", false);
    var shapes = GetBoolOption(options, "shapes", false);
    if (command == "plug-lab")
    {
        return await RunPlugLabCliAsync(options, port, timeoutSeconds).ConfigureAwait(false);
    }

    var client = new KasaLegacyClient(TimeSpan.FromSeconds(timeoutSeconds));
    var probe = new KasaReadOnlyProbe(client, new KasaSystemInfoParser(), new KasaEnergyParser(), new KasaCapabilityDetector());

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(GetIntOption(options, "overall-timeout", command == "scan" ? 60 : 15)));

    if (command == "probe")
    {
        if (!options.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
        {
            Console.Error.WriteLine("--host is required for probe.");
            return 2;
        }

        var result = await probe.ProbeAsync(host, port, includePrivacySensitive, cts.Token).ConfigureAwait(false);
        Console.WriteLine(KasaCliOutputFormatter.FormatProbeResult(result, includeIdentifiers, includeLocators, shapes));
        return result.IsSuccess ? 0 : 1;
    }

    if (!options.TryGetValue("cidr", out var cidr) || string.IsNullOrWhiteSpace(cidr))
    {
        Console.Error.WriteLine("--cidr is required for scan.");
        return 2;
    }

    var scanOptions = new KasaReadOnlyScanOptions
    {
        MaxHosts = GetIntOption(options, "max-hosts", KasaReadOnlyScanOptions.DefaultMaxHosts),
        MaxConcurrency = GetIntOption(options, "max-concurrency", KasaReadOnlyScanOptions.DefaultMaxConcurrency),
        RequireConfiguredNetwork = false,
        AllowedCidrs = []
    };
    var results = await probe.ScanCidrAsync(cidr, port, GetIntOption(options, "concurrency", 32), scanOptions, includePrivacySensitive, cts.Token)
        .ConfigureAwait(false);
    if (summary)
    {
        Console.WriteLine(KasaCliOutputFormatter.FormatScanSummary(results, shapes));
        return results.Count > 0 ? 0 : 1;
    }

    foreach (var result in results)
    {
        Console.WriteLine(KasaCliOutputFormatter.FormatProbeResult(result, includeIdentifiers, includeLocators, shapes));
    }

    return results.Count > 0 ? 0 : 1;
}

static async Task<int> RunPlugLabCliAsync(Dictionary<string, string> options, int port, int timeoutSeconds)
{
    if (!options.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
    {
        Console.Error.WriteLine("--host is required for plug-lab.");
        return 2;
    }

    if (!options.TryGetValue("device-id", out var deviceId) || string.IsNullOrWhiteSpace(deviceId))
    {
        Console.Error.WriteLine("--device-id is required for plug-lab.");
        return 2;
    }

    var target = options.TryGetValue("target", out var targetText) && !string.IsNullOrWhiteSpace(targetText)
        ? targetText.ToLowerInvariant()
        : "cycle";
    var expectedModel = options.TryGetValue("model", out var model) && !string.IsNullOrWhiteSpace(model)
        ? model
        : "HS105(US)";
    var sourceId = options.TryGetValue("source-id", out var sourceIdText) ? sourceIdText : null;
    var mac = options.TryGetValue("mac", out var macText) ? macText : null;
    var readbackTimeoutMs = GetIntOption(options, "readback-timeout-ms", 3000);
    var readbackPollMs = GetIntOption(options, "readback-poll-ms", 150);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(GetIntOption(options, "overall-timeout", 20)));
    var lab = new KasaPlugControlLab(
        new KasaLegacyLabClient(TimeSpan.FromSeconds(timeoutSeconds)),
        new KasaSystemInfoParser(),
        new KasaIdentityValidator());

    try
    {
        if (target is "exercise" or "schedule" or "schedule-pair" or "schedule-roundtrip" or "schedule-clear" or "schedule-one-time" or "schedule-reboot" or "countdown" or "countdown-on" or "countdown-reboot" or "app-countdown-off" or "app-countdown-on" or "app-countdown-on-from-off" or "away" or "diagnostics" or "inspect" or "led" or "brightness" or "watch-on" or "powercycle")
        {
            var labOptions = new KasaPlugLabOptions(
                host,
                port,
                deviceId,
                sourceId,
                mac,
                expectedModel,
                target,
                TimeSpan.FromMilliseconds(readbackTimeoutMs),
                TimeSpan.FromMilliseconds(readbackPollMs));
            var exercise = target switch
            {
                "schedule" => await lab.RunScheduleOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "schedule-pair" => await lab.RunSchedulePairOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "schedule-roundtrip" => await lab.RunScheduleRoundtripOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "schedule-clear" => await lab.RunScheduleClearOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "schedule-one-time" => await lab.RunScheduleOneTimeOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "schedule-reboot" => await lab.RunScheduleRebootOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "countdown" => await lab.RunCountdownOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "countdown-on" => await lab.RunCountdownOnOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "countdown-reboot" => await lab.RunCountdownRebootOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "app-countdown-off" => await lab.RunAppCountdownOffOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "app-countdown-on" => await lab.RunAppCountdownOnOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "app-countdown-on-from-off" => await lab.RunAppCountdownOnFromOffOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "away" => await lab.RunAwayOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "diagnostics" => await lab.RunDiagnosticsOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "inspect" => await lab.RunInspectOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "led" => await lab.RunLedOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "brightness" => await lab.RunBrightnessOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "watch-on" => await lab.RunWatchOnOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                "powercycle" => await lab.RunPowerCycleOnlyAsync(labOptions, cts.Token).ConfigureAwait(false),
                _ => await lab.RunExerciseAsync(labOptions, cts.Token).ConfigureAwait(false)
            };

            Console.WriteLine($"Plug {target} completed for model {exercise.Model ?? "unknown"} hw {exercise.HardwareVersion ?? "unknown"} sw {exercise.SoftwareVersion ?? "unknown"}.");
            Console.WriteLine($"Alias restored: {(string.Equals(exercise.OriginalAlias, exercise.FinalAlias, StringComparison.Ordinal) ? "yes" : "no")}; final state: {(exercise.FinalOn ? "On" : "Off")}");
            foreach (var step in exercise.Steps)
            {
                Console.WriteLine($"{step.Step}: {(step.Success ? "ok" : "note")} ({step.Elapsed.TotalMilliseconds:0} ms) {step.Detail}");
            }

            return exercise.FinalOn ? 0 : 1;
        }

        var result = await lab.RunAsync(new KasaPlugLabOptions(
            host,
            port,
            deviceId,
            sourceId,
            mac,
            expectedModel,
            target,
            TimeSpan.FromMilliseconds(readbackTimeoutMs),
            TimeSpan.FromMilliseconds(readbackPollMs)), cts.Token).ConfigureAwait(false);

        Console.WriteLine($"Plug lab completed for model {result.Model ?? "unknown"} hw {result.HardwareVersion ?? "unknown"} sw {result.SoftwareVersion ?? "unknown"}.");
        Console.WriteLine($"Started state: {(result.StartedOn ? "On" : "Off")}");
        foreach (var observation in result.Observations)
        {
            var write = observation.WriteElapsed is null ? "n/a" : $"{observation.WriteElapsed.Value.TotalMilliseconds:0} ms";
            var readback = $"{observation.ReadbackElapsed.TotalMilliseconds:0} ms";
            var state = observation.IsOn is null ? "unknown" : observation.IsOn.Value ? "On" : "Off";
            Console.WriteLine($"{observation.Step}: write={write}, readback={readback}, state={state}");
        }

        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or System.Net.Sockets.SocketException or OperationCanceledException)
    {
        Console.Error.WriteLine($"Plug lab failed: {ex.Message}");
        return 1;
    }
}

static async Task RunGatewayAsync(string[] args)
{
    if (args.FirstOrDefault()?.Equals("--help", StringComparison.OrdinalIgnoreCase) == true)
    {
        PrintUsage();
        return;
    }

    var builder = WebApplication.CreateBuilder(args);
    builder.Services
        .AddOptions<KasaGatewayOptions>()
        .BindConfiguration(KasaGatewayOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services
        .AddOptions<KasaGatewayOptions.OutboxSection>()
        .BindConfiguration(KasaGatewayOptions.OutboxSection.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddSingleton<IKasaLegacyClient>(sp =>
    {
        var gatewayOptions = sp.GetRequiredService<IOptions<KasaGatewayOptions>>().Value;
        return new KasaLegacyClient(TimeSpan.FromSeconds(gatewayOptions.SocketTimeoutSeconds));
    });
    builder.Services.AddSingleton(sp =>
    {
        var gatewayOptions = sp.GetRequiredService<IOptions<KasaGatewayOptions>>().Value;
        return new KasaLegacyLabClient(TimeSpan.FromSeconds(gatewayOptions.SocketTimeoutSeconds));
    });
    builder.Services.AddSingleton<KasaSystemInfoParser>();
    builder.Services.AddSingleton<KasaEnergyParser>();
    builder.Services.AddSingleton<KasaReadMetadataParser>();
    builder.Services.AddSingleton<KasaCapabilityDetector>();
    builder.Services.AddSingleton<KasaIdentityValidator>();
    builder.Services.AddSingleton<KasaDeviceRegistry>();
    builder.Services.AddSingleton<KasaDisplayTimeZoneResolver>();
    builder.Services.AddSingleton<KasaReadOnlyProbe>();
    builder.Services.AddSingleton<KasaAdminService>();
    builder.Services.AddSingleton<KasaDeviceCommandService>();
    builder.Services.AddSingleton<KasaDeviceInteractionState>();
    builder.Services.AddSingleton<KasaDevicePoller>();
    builder.Services.AddSingleton<KasaGatewayState>();
    builder.Services.AddSingleton<KasaGatewayTelemetry>();
    builder.Services.AddSingleton<IKasaMacAddressLookup, KasaMacAddressResolver>();
    builder.Services.AddSingleton<KasaDeviceLocator>();
    builder.Services.AddSingleton<KasaGatewayWorker>();
    if (!builder.Environment.IsEnvironment("Testing"))
        builder.Services.AddHostedService(sp => sp.GetRequiredService<KasaGatewayWorker>());
    var outboxConfig = builder.Configuration.GetSection(KasaGatewayOptions.OutboxSection.SectionName).Get<KasaGatewayOptions.OutboxSection>();
    var dbPath = !string.IsNullOrWhiteSpace(outboxConfig?.DbPath)
        ? outboxConfig.DbPath
        : Path.Combine(builder.Environment.ContentRootPath, "outbox.db");
    builder.Services.AddDbContext<OutboxDbContext>(options => options.UseSqlite($"Data Source={dbPath};Default Timeout=30", sqlite => sqlite.CommandTimeout(30)));
    builder.Services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
    builder.Services.AddScoped<KasaOutboxWriter>();
    builder.Services.AddHttpClient("KasaPowerApi", (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<KasaGatewayOptions.OutboxSection>>().Value;
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        client.Timeout = TimeSpan.FromSeconds(30);
    }).AddStandardResilienceHandler();
    builder.Services.AddSingleton<KasaOutboxForwarder>();
    if (!builder.Environment.IsEnvironment("Testing"))
        builder.Services.AddHostedService(sp => sp.GetRequiredService<KasaOutboxForwarder>());
    builder.Services.AddHealthChecks().AddCheck<KasaGatewayHealthCheck>("tplink-kasa-gateway");
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    builder.Services.AddMudServices();

    var app = builder.Build();
    var gatewayStartedAtUtc = DateTime.UtcNow;

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
            db,
            KasaOutboxPayloadTypes.Energy,
            KasaOutboxPayloadTypes.EnergyVersion).ConfigureAwait(false);
    }

    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResultStatusCodes =
        {
            [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
            [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
            [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        }
    });

    app.MapGet("/gateway-health", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await state.GetHealthAsync(cancellationToken));
    });
    app.MapGet("/diagnostics/health", async (HttpContext httpContext, KasaGatewayState state, KasaOutboxForwarder forwarder, OutboxDbContext db, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var status = await CreateKasaDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, state, forwarder, db, gatewayOptions.Value, cancellationToken).ConfigureAwait(false);
        return Results.Ok(status.Health);
    });
    app.MapGet("/diagnostics/status", async (HttpContext httpContext, KasaGatewayState state, KasaOutboxForwarder forwarder, OutboxDbContext db, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await CreateKasaDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, state, forwarder, db, gatewayOptions.Value, cancellationToken).ConfigureAwait(false));
    });
    app.MapGet("/diagnostics/outbox", async (HttpContext httpContext, OutboxDbContext db, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken).ConfigureAwait(false));
    });
    app.MapGet("/diagnostics/devices", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await state.GetReviewStatusAsync(cancellationToken).ConfigureAwait(false));
    });
    app.MapGet("/status-review", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await state.GetReviewStatusAsync(cancellationToken));
    });
    app.MapGet("/status-review/current", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await state.GetReviewCurrentStatusAsync(cancellationToken));
    });
    app.MapGet("/devices", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await state.GetDevicesAsync(cancellationToken));
    });
    app.MapGet("/devices/search", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, string? q, string? model, string? kind, string? capability, string? metadataCapability, bool? online, bool? degraded, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var request = new KasaDeviceSearchRequest(q, model, kind, capability, metadataCapability, online, degraded);
        return Results.Ok(await state.SearchDevicesAsync(request, cancellationToken));
    });
    app.MapGet("/devices/{sourceId}", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, string sourceId, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var device = await state.GetDeviceBySourceIdAsync(sourceId, cancellationToken);
        return device is null ? Results.NotFound() : Results.Ok(device);
    });
    app.MapPost("/devices/{sourceId}/refresh-details", async (HttpContext httpContext, KasaAdminService adminService, IOptions<KasaGatewayOptions> gatewayOptions, string sourceId, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await adminService.RefreshDeviceDetailsAsync(sourceId, cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    app.MapPost("/devices/{sourceId}/commands/alias", async (HttpContext httpContext, KasaDeviceCommandService commandService, IOptions<KasaGatewayOptions> gatewayOptions, string sourceId, KasaAliasCommandRequest request, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await commandService.SetAliasAsync(sourceId, request.Alias, cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    app.MapPost("/devices/{sourceId}/commands/power", async (HttpContext httpContext, KasaDeviceCommandService commandService, IOptions<KasaGatewayOptions> gatewayOptions, string sourceId, KasaPowerCommandRequest request, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await commandService.SetPowerAsync(sourceId, request.IsOn, request.OutletIndex, cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    app.MapPost("/devices/{sourceId}/commands/dimmer", async (HttpContext httpContext, KasaDeviceCommandService commandService, IOptions<KasaGatewayOptions> gatewayOptions, string sourceId, KasaDimmerCommandRequest request, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await commandService.SetDimmerBrightnessAsync(sourceId, request.Brightness, cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    app.MapPost("/devices/{sourceId}/commands/light", async (HttpContext httpContext, KasaDeviceCommandService commandService, IOptions<KasaGatewayOptions> gatewayOptions, string sourceId, KasaLightCommandRequest request, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await commandService.SetLightAsync(sourceId, request, cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });
    app.MapGet("/status", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await state.GetStatusAsync(cancellationToken));
    });
    app.MapGet("/inventory", async (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions, CancellationToken cancellationToken) =>
    {
        if (!KasaGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await state.GetInventoryAsync(cancellationToken));
    });

    await app.RunAsync().ConfigureAwait(false);
}

static async Task<GatewayDiagnosticStatusResponse> CreateKasaDiagnosticStatusAsync(
    DateTime startedAtUtc,
    string environmentName,
    KasaGatewayState state,
    KasaOutboxForwarder forwarder,
    OutboxDbContext db,
    KasaGatewayOptions options,
    CancellationToken cancellationToken)
{
    var now = DateTime.UtcNow;
    var status = await state.GetStatusAsync(cancellationToken).ConfigureAwait(false);
    var offline = Math.Max(0, status.ConfiguredDeviceCount - status.OnlineDeviceCount);
    var counts = new GatewayDeviceCounts(status.ConfiguredDeviceCount, status.OnlineDeviceCount, status.DegradedDeviceCount, offline);
    var outboxState = forwarder.FailedCount > 0 ? "failed-records-present" : forwarder.PendingCount > 0 ? "pending-forward" : "current";
    var apiSyncState = string.IsNullOrWhiteSpace(forwarder.LastError) ? "healthy" : "error";
    var healthState = !string.IsNullOrWhiteSpace(status.LastError) || offline > 0 || apiSyncState == "error"
        ? GatewayHealthState.Critical
        : status.DegradedDeviceCount > 0 || forwarder.FailedCount > 0 ? GatewayHealthState.Warning : GatewayHealthState.Healthy;

    return new GatewayDiagnosticStatusResponse(
        "1.0",
        new GatewayIdentity(options.GatewayId, "TP-Link Kasa Gateway", GatewayDomain.Power, options.GatewayId, RuntimeHost: Environment.MachineName),
        new GatewayRuntimeInfo(startedAtUtc, now, now - startedAtUtc, environmentName),
        new GatewayHealthSnapshot(
            healthState,
            now,
            BuildKasaAlerts(status, offline),
            healthState == GatewayHealthState.Healthy ? GatewaySampleState.Live : GatewaySampleState.Error,
            outboxState,
            apiSyncState),
        counts,
        await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken).ConfigureAwait(false),
        CreateTelemetryDiagnostics("hvo-tplink-kasa", "hvo.tplinkkasa"),
        new Dictionary<string, string>
        {
            ["health"] = "/diagnostics/health",
            ["status"] = "/diagnostics/status",
            ["outbox"] = "/diagnostics/outbox",
            ["devices"] = "/diagnostics/devices",
            ["legacyGatewayHealth"] = "/gateway-health",
            ["legacyStatus"] = "/status",
            ["legacyDevices"] = "/devices",
        });
}

static IReadOnlyList<GatewayHealthAlert> BuildKasaAlerts(KasaGatewayStatusResponse status, int offline)
{
    var alerts = new List<GatewayHealthAlert>();
    if (!string.IsNullOrWhiteSpace(status.LastError))
        alerts.Add(new GatewayHealthAlert("kasa-poll-error", GatewayAlertSeverity.Critical, status.LastError));
    if (offline > 0)
        alerts.Add(new GatewayHealthAlert("kasa-devices-offline", GatewayAlertSeverity.Critical, $"{offline} configured Kasa device(s) are offline."));
    if (status.DegradedDeviceCount > 0)
        alerts.Add(new GatewayHealthAlert("kasa-devices-degraded", GatewayAlertSeverity.Warning, $"{status.DegradedDeviceCount} Kasa device(s) are degraded."));
    return alerts;
}

static GatewayTelemetryDiagnostics CreateTelemetryDiagnostics(string defaultServiceName, string sourceName) => new(
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")),
    Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? defaultServiceName,
    [
        GatewayTelemetryConventions.MetricNames.OutboxDepth,
        GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess,
        GatewayTelemetryConventions.MetricNames.OutboxForwardFailure,
        GatewayTelemetryConventions.MetricNames.DeviceFreshnessSeconds,
        GatewayTelemetryConventions.MetricNames.DevicePollFailure,
    ],
    [sourceName]);

static void PrintUsage()
{
    Console.WriteLine("TP-Link/Kasa read-only prototype utility");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa -- probe --host <ip-or-host> [--port 9999]");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa -- scan --cidr <x.x.x.x/nn> [--port 9999] [--concurrency 32] [--max-hosts 256] [--summary true] [--shapes true] [--include-privacy-sensitive true]");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa -- plug-lab --host <ip-or-host> --device-id <device-id> [--mac <mac>] [--model HS105(US)] [--target cycle|toggle|on|off|exercise|schedule|schedule-pair|schedule-roundtrip|schedule-clear|schedule-one-time|schedule-reboot|countdown|countdown-on|countdown-reboot|app-countdown-off|app-countdown-on|app-countdown-on-from-off|away|diagnostics|inspect|led|brightness|watch-on|powercycle]");
    Console.WriteLine();
    Console.WriteLine("Only allowlisted read-only Kasa commands are sent. Wi-Fi scan shapes require --include-privacy-sensitive true and never print raw values unless future code explicitly adds them.");
    Console.WriteLine("plug-lab is a terminal-only guarded write lab for a single non-critical plug; it validates identity first and blocks network, MAC, cloud, reset, and factory commands.");
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < values.Length; i++)
    {
        var value = values[i];
        if (!value.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = value[2..];
        if (i + 1 < values.Length && !values[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[key] = values[++i];
        }
        else
        {
            result[key] = "true";
        }
    }

    return result;
}

static int GetIntOption(Dictionary<string, string> options, string name, int defaultValue) =>
    options.TryGetValue(name, out var text) && int.TryParse(text, out var value) ? value : defaultValue;

static bool GetBoolOption(Dictionary<string, string> options, string name, bool defaultValue) =>
    options.TryGetValue(name, out var text) && bool.TryParse(text, out var value) ? value : defaultValue;
