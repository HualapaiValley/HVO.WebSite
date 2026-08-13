using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal interface IHomeAssistantRegistryClient : IAsyncDisposable
{
    string Version { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<JsonElement[]> ListEntitiesAsync(CancellationToken cancellationToken);
    Task<JsonElement> FindRelatedAsync(string entityId, CancellationToken cancellationToken);
    Task<string> CreateBackupAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<JsonElement>> GetLovelaceConfigurationsAsync(CancellationToken cancellationToken);
    Task<JsonElement> RenameAsync(string sourceEntityId, string targetEntityId, CancellationToken cancellationToken);
    Task<JsonElement[]> ListStatesAsync(CancellationToken cancellationToken);
    Task<JsonElement?> GetEnergyPreferencesAsync(CancellationToken cancellationToken);
    Task<JsonElement> SaveEnergyPreferencesAsync(
        IReadOnlyList<JsonElement> energySources,
        IReadOnlyList<JsonElement> deviceConsumption,
        IReadOnlyList<JsonElement> waterConsumption,
        CancellationToken cancellationToken);
    Task<JsonElement> ValidateEnergyAsync(CancellationToken cancellationToken);
}

internal sealed class HomeAssistantRegistryClient(Uri endpoint, string accessToken) : IHomeAssistantRegistryClient
{
    private const int MaxMessageBytes = 8 * 1024 * 1024;
    private readonly ClientWebSocket socket = new();
    private int commandId;

    public string Version { get; private set; } = string.Empty;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await socket.ConnectAsync(endpoint, cancellationToken);
        using var required = await ReceiveAsync(cancellationToken);
        if (required.RootElement.GetProperty("type").GetString() != "auth_required")
            throw new InvalidOperationException("Home Assistant did not request authentication.");
        Version = required.RootElement.GetProperty("ha_version").GetString() ?? string.Empty;
        await SendAsync(new { type = "auth", access_token = accessToken }, cancellationToken);
        using var authenticated = await ReceiveAsync(cancellationToken);
        if (authenticated.RootElement.GetProperty("type").GetString() != "auth_ok")
            throw new InvalidOperationException("Home Assistant authentication failed.");
    }

    public async Task<JsonElement[]> ListEntitiesAsync(CancellationToken cancellationToken)
    {
        var result = await CommandAsync(new { type = "config/entity_registry/list" }, cancellationToken);
        return result.EnumerateArray().Select(static item => item.Clone()).ToArray();
    }

    public Task<JsonElement> FindRelatedAsync(string entityId, CancellationToken cancellationToken) =>
        CommandAsync(new { type = "search/related", item_type = "entity", item_id = entityId }, cancellationToken);

    public async Task<string> CreateBackupAsync(string name, CancellationToken cancellationToken)
    {
        var agentInfo = await CommandAsync(new { type = "backup/agents/info" }, cancellationToken);
        var agentIds = agentInfo.GetProperty("agents").EnumerateArray()
            .Select(agent => agent.GetProperty("agent_id").GetString())
            .Where(static agentId => !string.IsNullOrWhiteSpace(agentId))
            .ToArray();
        if (agentIds.Length == 0)
            throw new InvalidOperationException("Home Assistant has no available backup agent.");
        var backupName = $"{name} {DateTime.UtcNow:yyyyMMddTHHmmssZ}";
        await CommandAsync(new
        {
            type = "backup/generate",
            agent_ids = agentIds,
            include_database = true,
            include_homeassistant = true,
            include_all_addons = false,
            name = backupName
        }, cancellationToken);

        while (true)
        {
            var info = await CommandAsync(new { type = "backup/info" }, cancellationToken);
            if (info.GetProperty("agent_errors").EnumerateObject().Any())
                throw new InvalidOperationException("Home Assistant could not query all configured backup agents.");
            var completed = info.GetProperty("backups").EnumerateArray()
                .SingleOrDefault(candidate => candidate.GetProperty("name").GetString() == backupName);
            if (completed.ValueKind != JsonValueKind.Undefined)
            {
                EnsureBackupSucceeded(completed);
                return completed.GetProperty("backup_id").GetString()
                    ?? throw new InvalidOperationException("Completed Home Assistant backup has no backup ID.");
            }
            if (info.TryGetProperty("last_action_event", out var action)
                && action.ValueKind == JsonValueKind.Object
                && action.TryGetProperty("manager_state", out var managerState)
                && managerState.GetString() == "create_backup"
                && action.TryGetProperty("state", out var state)
                && state.GetString() == "failed")
                throw new InvalidOperationException("Home Assistant migration backup failed.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    public async Task<IReadOnlyList<JsonElement>> GetLovelaceConfigurationsAsync(CancellationToken cancellationToken)
    {
        var configurations = new List<JsonElement>();
        try
        {
            configurations.Add(await CommandAsync(new { type = "lovelace/config" }, cancellationToken));
        }
        catch (HomeAssistantCommandException exception) when (exception.Code == "not_found")
        {
        }

        var dashboards = await CommandAsync(new { type = "lovelace/dashboards/list" }, cancellationToken);
        foreach (var dashboard in dashboards.EnumerateArray())
        {
            var urlPath = dashboard.GetProperty("url_path").GetString();
            if (string.IsNullOrWhiteSpace(urlPath))
                continue;
            configurations.Add(await CommandAsync(new { type = "lovelace/config", url_path = urlPath }, cancellationToken));
        }
        return configurations;
    }

    public Task<JsonElement> RenameAsync(string sourceEntityId, string targetEntityId, CancellationToken cancellationToken) =>
        CommandAsync(new { type = "config/entity_registry/update", entity_id = sourceEntityId, new_entity_id = targetEntityId }, cancellationToken);

    public async Task<JsonElement[]> ListStatesAsync(CancellationToken cancellationToken)
    {
        var result = await CommandAsync(new { type = "get_states" }, cancellationToken);
        return result.EnumerateArray().Select(static item => item.Clone()).ToArray();
    }

    public async Task<JsonElement?> GetEnergyPreferencesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await CommandAsync(new { type = "energy/get_prefs" }, cancellationToken);
        }
        catch (HomeAssistantCommandException exception) when (exception.Code == "not_found")
        {
            return null;
        }
    }

    public Task<JsonElement> SaveEnergyPreferencesAsync(
        IReadOnlyList<JsonElement> energySources,
        IReadOnlyList<JsonElement> deviceConsumption,
        IReadOnlyList<JsonElement> waterConsumption,
        CancellationToken cancellationToken) =>
        CommandAsync(new
        {
            type = "energy/save_prefs",
            energy_sources = energySources,
            device_consumption = deviceConsumption,
            device_consumption_water = waterConsumption,
        }, cancellationToken);

    public Task<JsonElement> ValidateEnergyAsync(CancellationToken cancellationToken) =>
        CommandAsync(new { type = "energy/validate" }, cancellationToken);

    private static void EnsureBackupSucceeded(JsonElement backup)
    {
        if (!backup.GetProperty("homeassistant_included").GetBoolean()
            || !backup.GetProperty("database_included").GetBoolean()
            || backup.GetProperty("agents").EnumerateObject().Count() == 0
            || backup.GetProperty("failed_agent_ids").GetArrayLength() != 0
            || backup.GetProperty("failed_addons").GetArrayLength() != 0
            || backup.GetProperty("failed_folders").GetArrayLength() != 0)
            throw new InvalidOperationException("Home Assistant migration backup did not complete successfully.");
    }

    private async Task<JsonElement> CommandAsync(object command, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref commandId);
        var payload = JsonSerializer.SerializeToElement(command, JsonOptions);
        var values = payload.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.Clone(),
            StringComparer.Ordinal);
        values["id"] = JsonSerializer.SerializeToElement(id);
        await SendAsync(values, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var response = await ReceiveAsync(timeout.Token);
            var root = response.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
                continue;
            if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
            {
                var code = root.TryGetProperty("error", out var errorElement)
                    && errorElement.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString() ?? "unknown_error"
                    : "unknown_error";
                var message = root.TryGetProperty("error", out errorElement)
                    && errorElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString() ?? "unknown error"
                    : "unknown error";
                throw new HomeAssistantCommandException(code, message);
            }
            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task SendAsync(object value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task<JsonDocument> ReceiveAsync(CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(16_384);
        try
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(rented, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Home Assistant closed the WebSocket session.");
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new WebSocketException("Home Assistant returned a non-text message.");
                stream.Write(rented, 0, result.Count);
                if (stream.Length > MaxMessageBytes)
                    throw new InvalidOperationException("Home Assistant returned an oversized WebSocket message.");
            } while (!result.EndOfMessage);
            return JsonDocument.Parse(stream.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "complete", timeout.Token);
        }
        socket.Dispose();
    }
}

internal sealed class HomeAssistantCommandException(string code, string message)
    : InvalidOperationException($"Home Assistant command failed ({code}): {message}")
{
    public string Code { get; } = code;
}
