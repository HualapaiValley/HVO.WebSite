using HVO.Tools.HomeAssistantEntityMigration;

var mode = args.ElementAtOrDefault(0);
var validArguments = mode switch
{
    "--check" or "--apply" => args.Length is 1 or 2,
    "--energy-check" or "--energy-apply" => args.Length is 1 or 2,
    "--energy-audit" => args.Length == 1,
    "--backup" => args.Length == 1,
    "--rollback" => args.Length is 2 or 3,
    _ => false
};
if (!validArguments)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  HVO.Tools.HomeAssistantEntityMigration --check|--apply [manifest-path]");
    Console.Error.WriteLine("  HVO.Tools.HomeAssistantEntityMigration --energy-check|--energy-apply [manifest-path]");
    Console.Error.WriteLine("  HVO.Tools.HomeAssistantEntityMigration --energy-audit");
    Console.Error.WriteLine("  HVO.Tools.HomeAssistantEntityMigration --backup");
    Console.Error.WriteLine("  HVO.Tools.HomeAssistantEntityMigration --rollback <backup-path> [manifest-path]");
    return 2;
}

var defaultManifest = Path.Combine(AppContext.BaseDirectory, "davis-readable-ids.json");
var defaultEnergyManifest = Path.Combine(AppContext.BaseDirectory, "hvo-energy-preferences.json");
var energyMode = mode is "--energy-check" or "--energy-apply";
var manifestPath = mode == "--rollback"
    ? args.ElementAtOrDefault(2) ?? defaultManifest
    : args.ElementAtOrDefault(1) ?? (energyMode ? defaultEnergyManifest : defaultManifest);
var rollbackPath = mode == "--rollback" ? args.ElementAtOrDefault(1) : null;

var token = Environment.GetEnvironmentVariable("HOME_ASSISTANT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("HOME_ASSISTANT_TOKEN is required.");
    return 2;
}
var baseUrl = Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_URL");
if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var homeAssistantUrl)
    || homeAssistantUrl.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("HVO_HOME_ASSISTANT_URL is required and must be an absolute HTTP or HTTPS URL.");
    return 2;
}
if (homeAssistantUrl.Scheme == "http"
    && !string.Equals(Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_ALLOW_INSECURE"), "true", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("HTTP sends the administrator token without transport encryption. Set HVO_HOME_ASSISTANT_ALLOW_INSECURE=true only for a trusted local Home Assistant network.");
    return 2;
}
var websocketUrl = new UriBuilder(homeAssistantUrl)
{
    Scheme = homeAssistantUrl.Scheme == "https" ? "wss" : "ws",
    Path = $"{homeAssistantUrl.AbsolutePath.TrimEnd('/')}/api/websocket"
}.Uri;
await using var client = new HomeAssistantRegistryClient(websocketUrl, token);
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
if (mode is "--apply" or "--energy-apply")
    cancellation.CancelAfter(TimeSpan.FromMinutes(30));
await client.ConnectAsync(cancellation.Token);
if (mode == "--energy-audit")
{
    await new EnergyAuditRunner(client).AuditAsync(cancellation.Token);
    return 0;
}
if (mode == "--backup")
{
    var backupId = await client.CreateBackupAsync("Before HVO off-grid Energy trial", cancellation.Token);
    Console.WriteLine("Created verified Home Assistant backup {0}.", backupId);
    return 0;
}
if (energyMode)
{
    var energyManifest = await EnergyPreferencesManifest.LoadAsync(manifestPath, cancellation.Token);
    var energyRunner = new EnergyPreferencesRunner(client, energyManifest);
    if (mode == "--energy-check")
        await energyRunner.CheckAsync(cancellation.Token);
    else
        await energyRunner.ApplyAsync(cancellation.Token);
    return 0;
}

var manifest = await EntityMigrationManifest.LoadAsync(manifestPath, cancellation.Token);
var backupPath = Path.Combine("artifacts", "home-assistant", $"{manifest.MigrationId}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.json");
var trackedConfigurationPath = Path.Combine(Directory.GetCurrentDirectory(), "deploy", "home-assistant", "configuration");
var runner = new EntityMigrationRunner(client, manifest, backupPath, trackedConfigurationPath);
if (mode == "--check")
    await runner.CheckAsync(cancellation.Token);
else if (mode == "--apply")
    await runner.ApplyAsync(cancellation.Token);
else
    await runner.RollbackAsync(rollbackPath!, cancellation.Token);
return 0;
