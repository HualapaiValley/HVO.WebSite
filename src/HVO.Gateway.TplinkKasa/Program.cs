using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Hosting;
using HVO.Gateway.TplinkKasa.Protocol;
using Microsoft.Extensions.Options;

var command = args.FirstOrDefault()?.ToLowerInvariant();
if (command is "probe" or "scan")
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
    builder.Services.AddSingleton<IKasaLegacyClient>(sp =>
    {
        var gatewayOptions = sp.GetRequiredService<IOptions<KasaGatewayOptions>>().Value;
        return new KasaLegacyClient(TimeSpan.FromSeconds(gatewayOptions.SocketTimeoutSeconds));
    });
    builder.Services.AddSingleton<KasaSystemInfoParser>();
    builder.Services.AddSingleton<KasaEnergyParser>();
    builder.Services.AddSingleton<KasaReadMetadataParser>();
    builder.Services.AddSingleton<KasaCapabilityDetector>();
    builder.Services.AddSingleton<KasaIdentityValidator>();
    builder.Services.AddSingleton<KasaDevicePoller>();
    builder.Services.AddSingleton<KasaGatewayState>();
    builder.Services.AddSingleton<KasaGatewayWorker>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<KasaGatewayWorker>());
    builder.Services.AddHealthChecks().AddCheck<KasaGatewayHealthCheck>("tplink-kasa-gateway");

    var app = builder.Build();

    app.MapHealthChecks("/health");

    app.MapGet("/gateway-health", (KasaGatewayState state) => Results.Ok(state.GetHealth()));
    app.MapGet("/status", (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions) =>
    {
        if (!HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(state.GetStatus());
    });
    app.MapGet("/inventory", (HttpContext httpContext, KasaGatewayState state, IOptions<KasaGatewayOptions> gatewayOptions) =>
    {
        if (!HasMatchingApiKey(httpContext, gatewayOptions.Value.ApiKey))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(state.GetInventory());
    });

    await app.RunAsync().ConfigureAwait(false);
}

static void PrintUsage()
{
    Console.WriteLine("TP-Link/Kasa read-only prototype utility");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa -- probe --host <ip-or-host> [--port 9999]");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa -- scan --cidr <x.x.x.x/nn> [--port 9999] [--concurrency 32] [--max-hosts 256] [--summary true] [--shapes true] [--include-privacy-sensitive true]");
    Console.WriteLine();
    Console.WriteLine("Only allowlisted read-only Kasa commands are sent. Wi-Fi scan shapes require --include-privacy-sensitive true and never print raw values unless future code explicitly adds them.");
}

static bool HasMatchingApiKey(HttpContext httpContext, string configuredApiKey)
{
    if (string.IsNullOrWhiteSpace(configuredApiKey) ||
        string.Equals(configuredApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
        configuredApiKey.Contains("__SET_", StringComparison.Ordinal))
    {
        return false;
    }

    return httpContext.Request.Headers.TryGetValue("X-Api-Key", out var providedApiKey)
        && providedApiKey.Count > 0
        && string.Equals(providedApiKey[0], configuredApiKey, StringComparison.Ordinal);
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
