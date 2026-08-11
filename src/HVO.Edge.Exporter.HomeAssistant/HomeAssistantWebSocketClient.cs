using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal interface IHomeAssistantEventSource
{
    Task RunSessionAsync(
        Func<IReadOnlyList<HomeAssistantState>, CancellationToken, Task> onSnapshot,
        Func<HomeAssistantState, CancellationToken, Task> onStateChanged,
        CancellationToken cancellationToken);
}

internal sealed class HomeAssistantWebSocketClient(
    IOptions<HomeAssistantExporterOptions> options,
    HomeAssistantExporterCredential credential) : IHomeAssistantEventSource
{
    private readonly HomeAssistantExporterOptions options = options.Value;

    public async Task RunSessionAsync(
        Func<IReadOnlyList<HomeAssistantState>, CancellationToken, Task> onSnapshot,
        Func<HomeAssistantState, CancellationToken, Task> onStateChanged,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
        await socket.ConnectAsync(new Uri(options.Endpoint!), timeout.Token);

        using (var required = await ReceiveCommandAsync(socket, cancellationToken))
        {
            if (required.RootElement.GetProperty("type").GetString() != "auth_required")
                throw new InvalidOperationException("Home Assistant did not request WebSocket authentication.");
        }
        await SendCommandAsync(socket, new { type = "auth", access_token = credential.AccessToken }, cancellationToken);
        using (var authenticated = await ReceiveCommandAsync(socket, cancellationToken))
        {
            if (authenticated.RootElement.GetProperty("type").GetString() != "auth_ok")
                throw new InvalidOperationException("Home Assistant WebSocket authentication failed.");
        }

        var mappedEntityIds = options.Mappings
            .SelectMany(static mapping => mapping.Entities)
            .Select(static binding => binding.EntityId!)
            .ToHashSet(StringComparer.Ordinal);
        var buffered = new Dictionary<string, HomeAssistantState>(StringComparer.Ordinal);

        await SendCommandAsync(socket, new { id = 1, type = "subscribe_events", event_type = "state_changed" }, cancellationToken);
        using (var subscriptionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            subscriptionTimeout.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
            var subscribed = false;
            while (!subscribed)
            {
                using var message = await ReceiveAsync(socket, subscriptionTimeout.Token);
                var root = message.RootElement;
                if (root.TryGetProperty("id", out var id) && id.GetInt32() == 1 && root.GetProperty("type").GetString() == "result")
                {
                    EnsureSuccess(root);
                    subscribed = true;
                }
                else if (TryParseEvent(root, out var changed) && mappedEntityIds.Contains(changed.EntityId))
                {
                    buffered[changed.EntityId] = changed;
                }
            }
        }
        await SendCommandAsync(socket, new { id = 2, type = "config/entity_registry/list_for_display" }, cancellationToken);
        using (var registryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            registryTimeout.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
            var validated = false;
            while (!validated)
            {
                using var message = await ReceiveAsync(socket, registryTimeout.Token);
                var root = message.RootElement;
                if (root.TryGetProperty("id", out var id) && id.GetInt32() == 2 && root.GetProperty("type").GetString() == "result")
                {
                    EnsureSuccess(root);
                    ValidateRegistry(root.GetProperty("result"));
                    validated = true;
                }
                else if (TryParseEvent(root, out var changed) && mappedEntityIds.Contains(changed.EntityId))
                {
                    buffered[changed.EntityId] = changed;
                }
            }
        }
        await SendCommandAsync(socket, new { id = 3, type = "get_states" }, cancellationToken);

        IReadOnlyList<HomeAssistantState>? snapshot = null;
        using var snapshotTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        snapshotTimeout.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
        while (snapshot is null)
        {
            using var message = await ReceiveAsync(socket, snapshotTimeout.Token);
            var root = message.RootElement;
            if (root.TryGetProperty("id", out var id) && id.GetInt32() == 3 && root.GetProperty("type").GetString() == "result")
            {
                EnsureSuccess(root);
                snapshot = root.GetProperty("result").EnumerateArray().Select(ParseState).ToArray();
            }
            else if (TryParseEvent(root, out var changed) && mappedEntityIds.Contains(changed.EntityId))
            {
                buffered[changed.EntityId] = changed;
            }
        }
        await onSnapshot(snapshot, cancellationToken);
        foreach (var changed in buffered.Values.OrderBy(static state => state.LastUpdatedUtc))
            await onStateChanged(changed, cancellationToken);

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var message = await ReceiveAsync(socket, cancellationToken);
            if (TryParseEvent(message.RootElement, out var changed) && mappedEntityIds.Contains(changed.EntityId))
                await onStateChanged(changed, cancellationToken);
        }
    }

    private void ValidateRegistry(JsonElement result)
    {
        var entries = result.ValueKind == JsonValueKind.Array
            ? result.EnumerateArray()
            : result.GetProperty("entities").EnumerateArray();
        var byEntity = entries
            .Where(static entry => entry.TryGetProperty("ei", out _) && entry.TryGetProperty("pl", out _))
            .ToDictionary(static entry => entry.GetProperty("ei").GetString()!, static entry => entry.GetProperty("pl").GetString()!, StringComparer.Ordinal);
        foreach (var mapping in options.Mappings)
        foreach (var binding in mapping.Entities)
        {
            if (!byEntity.TryGetValue(binding.EntityId!, out var platform))
                throw new InvalidOperationException($"Configured Home Assistant entity {binding.EntityId} is not registered.");
            if (!string.Equals(platform, mapping.ExpectedPlatform, StringComparison.Ordinal))
                throw new InvalidOperationException($"Configured Home Assistant entity {binding.EntityId} has an unexpected platform.");
        }
    }

    private static void EnsureSuccess(JsonElement root)
    {
        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
            throw new InvalidOperationException("Home Assistant WebSocket command failed.");
    }

    private static bool TryParseEvent(JsonElement root, out HomeAssistantState state)
    {
        state = null!;
        if (root.TryGetProperty("type", out var type) && type.GetString() == "event"
            && root.TryGetProperty("event", out var eventElement)
            && eventElement.TryGetProperty("event_type", out var eventType) && eventType.GetString() == "state_changed"
            && eventElement.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("new_state", out var newState) && newState.ValueKind == JsonValueKind.Object)
            {
                state = ParseState(newState);
                return true;
            }
            if (data.TryGetProperty("entity_id", out var entityId))
            {
                state = new HomeAssistantState(
                    entityId.GetString()!,
                    "unavailable",
                    JsonSerializer.SerializeToElement(new { }),
                    eventElement.GetProperty("time_fired").GetDateTimeOffset());
                return true;
            }
        }
        return false;
    }

    private static HomeAssistantState ParseState(JsonElement value) => new(
        value.GetProperty("entity_id").GetString()!,
        value.GetProperty("state").GetString()!,
        value.GetProperty("attributes").Clone(),
        value.GetProperty("last_updated").GetDateTimeOffset());

    private async Task<JsonDocument> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(16384);
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
                    throw new WebSocketException("Home Assistant returned a non-text WebSocket message.");
                stream.Write(rented, 0, result.Count);
                if (stream.Length > options.MaxMessageBytes)
                    throw new InvalidOperationException("Home Assistant WebSocket message exceeded the configured size limit.");
            } while (!result.EndOfMessage);
            return JsonDocument.Parse(stream.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async Task<JsonDocument> ReceiveCommandAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
        return await ReceiveAsync(socket, timeout.Token);
    }

    private async Task SendCommandAsync(ClientWebSocket socket, object value, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
        await SendAsync(socket, value, timeout.Token);
    }

    private static Task SendAsync(ClientWebSocket socket, object value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}
