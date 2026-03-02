using HVO.NinaClient.Models;
using HVO.Core.Results;
using HVO.NinaClient.Infrastructure;
using HVO.NinaClient.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace HVO.NinaClient;

/// <summary>
/// Event arguments for NINA WebSocket events
/// </summary>
public class NinaEventArgs : EventArgs
{
    public NinaEventType EventType { get; }
    public object? EventData { get; }
    public NinaWebSocketResponse? RawResponse { get; }

    public NinaEventArgs(NinaEventType eventType, object? eventData, NinaWebSocketResponse? rawResponse)
    {
        EventType = eventType;
        EventData = eventData;
        RawResponse = rawResponse;
    }
}

/// <summary>
/// Interface for NINA WebSocket client with enhanced functionality
/// </summary>
public interface INinaWebSocketClient : IDisposable
{
    /// <summary>
    /// Event raised when any NINA event is received
    /// </summary>
    event EventHandler<NinaEventArgs>? EventReceived;

    /// <summary>
    /// Event raised when connection state changes
    /// </summary>
    event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    event EventHandler<Exception>? ErrorOccurred;

    /// <summary>
    /// Gets the current connection state
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connect to the NINA WebSocket server
    /// </summary>
    Task<Result<bool>> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the NINA WebSocket server
    /// </summary>
    Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Move mount axis manually
    /// </summary>
    Task<Result<bool>> MoveMountAxisAsync(MountDirection direction, double rate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop mount axis movement
    /// </summary>
    Task<Result<bool>> StopMountMovementAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Start TPPA polar alignment
    /// </summary>
    Task<Result<bool>> StartTppaAlignmentAsync(TppaCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop TPPA polar alignment
    /// </summary>
    Task<Result<bool>> StopTppaAlignmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause TPPA polar alignment
    /// </summary>
    Task<Result<bool>> PauseTppaAlignmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume TPPA polar alignment
    /// </summary>
    Task<Result<bool>> ResumeTppaAlignmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get target filter from networked filter wheel
    /// </summary>
    Task<Result<bool>> GetTargetFilterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signal filter changed to networked filter wheel
    /// </summary>
    Task<Result<bool>> SignalFilterChangedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test message to verify WebSocket connectivity
    /// </summary>
    Task<Result<bool>> SendTestMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover available WebSocket endpoints on the NINA server
    /// </summary>
    Task<Result<string>> DiscoverWebSocketEndpointAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets diagnostics information about the WebSocket client
    /// </summary>
    NinaWebSocketDiagnostics GetDiagnostics();
}

/// <summary>
/// Configuration options for NINA WebSocket client with validation
/// </summary>
public record NinaWebSocketOptions
{
    /// <summary>
    /// The base URI for the NINA WebSocket server (default: ws://localhost:1888/v2)
    /// </summary>
    [Required]
    public string BaseUri { get; init; } = "ws://localhost:1888/v2";

    /// <summary>
    /// Connection timeout in milliseconds (default: 5000ms)
    /// </summary>
    [Range(1000, 60000)]
    public int ConnectionTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Keep-alive interval in milliseconds (default: 30000ms)
    /// </summary>
    [Range(1000, 300000)]
    public int KeepAliveIntervalMs { get; init; } = 30000;

    /// <summary>
    /// Buffer size for WebSocket messages (default: 4096 bytes)
    /// </summary>
    [Range(512, 65536)]
    public int BufferSize { get; init; } = 4096;

    /// <summary>
    /// Maximum number of reconnection attempts (default: 5)
    /// </summary>
    [Range(0, 20)]
    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>
    /// Delay between reconnection attempts in milliseconds (default: 2000ms)
    /// </summary>
    [Range(500, 30000)]
    public int ReconnectDelayMs { get; init; } = 2000;

    /// <summary>
    /// Enable circuit breaker for WebSocket connections
    /// </summary>
    public bool EnableCircuitBreaker { get; init; } = true;

    /// <summary>
    /// Circuit breaker failure threshold
    /// </summary>
    [Range(1, 20)]
    public int CircuitBreakerFailureThreshold { get; init; } = 3;

    /// <summary>
    /// Circuit breaker timeout in seconds
    /// </summary>
    [Range(10, 300)]
    public int CircuitBreakerTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Validates the configuration options
    /// </summary>
    /// <returns>Validation result</returns>
    public ValidationResult Validate()
    {
        var context = new ValidationContext(this);
        var results = new List<ValidationResult>();
        
        if (!Validator.TryValidateObject(this, context, results, true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            return new ValidationResult($"Configuration validation failed: {errors}");
        }

        // Additional custom validation for WebSocket URI
        if (!Uri.IsWellFormedUriString(BaseUri, UriKind.Absolute))
        {
            return new ValidationResult($"BaseUri '{BaseUri}' is not a valid absolute URI");
        }

        var uri = new Uri(BaseUri);
        if (uri.Scheme != "ws" && uri.Scheme != "wss")
        {
            return new ValidationResult($"BaseUri scheme must be ws or wss, got: {uri.Scheme}");
        }

        return ValidationResult.Success!;
    }

    /// <summary>
    /// Validates and throws if configuration is invalid
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    public void ValidateAndThrow()
    {
        var result = Validate();
        if (result != ValidationResult.Success)
        {
            throw new ArgumentException(result.ErrorMessage, nameof(NinaWebSocketOptions));
        }
    }
}

/// <summary>
/// NINA WebSocket client implementation with enhanced resilience and memory optimization
/// </summary>
public class NinaWebSocketClient : INinaWebSocketClient
{
    private readonly ILogger<NinaWebSocketClient> _logger;
    private readonly NinaWebSocketOptions _options;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BufferManager _bufferManager;
    private readonly CircuitBreaker? _circuitBreaker;

    // Enhanced connection management with per-channel tracking
    private readonly ConcurrentDictionary<string, ClientWebSocket> _channelConnections = new();
    private readonly ConcurrentDictionary<string, Task> _channelReceiveTasks = new();
    private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics = new();
    
    private bool _disposed;
    private int _reconnectAttempts;

    public event EventHandler<NinaEventArgs>? EventReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public bool IsConnected => _channelConnections.Values.Any(ws => ws.State == WebSocketState.Open);

    public NinaWebSocketClient(ILogger<NinaWebSocketClient> logger, IOptions<NinaWebSocketOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Validate configuration at startup
        _options.ValidateAndThrow();

        // Initialize buffer manager for memory optimization
        _bufferManager = new BufferManager();

        // Initialize circuit breaker if enabled
        if (_options.EnableCircuitBreaker)
        {
            _circuitBreaker = new CircuitBreaker(
                _options.CircuitBreakerFailureThreshold,
                TimeSpan.FromSeconds(_options.CircuitBreakerTimeoutSeconds),
                logger);
                
            _logger.LogInformation("WebSocket circuit breaker enabled - FailureThreshold: {FailureThreshold}, Timeout: {Timeout}s",
                _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerTimeoutSeconds);
        }
    }

    public async Task<Result<bool>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure(new ObjectDisposedException(nameof(NinaWebSocketClient)));
        }

        if (_circuitBreaker != null)
        {
            return await _circuitBreaker.ExecuteAsync(() => ConnectInternalAsync(cancellationToken));
        }

        return await ConnectInternalAsync(cancellationToken);
    }

    private async Task<Result<bool>> ConnectInternalAsync(CancellationToken cancellationToken)
    {
        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                _logger.LogDebug("Already connected to NINA WebSocket server");
                return Result<bool>.Success(true);
            }

            _logger.LogInformation("Connecting to NINA WebSocket server at {BaseUri}", _options.BaseUri);

            // Connect to all AsyncAPI channels as specified in NINA API documentation
            var channelsToConnect = new[] { "/socket", "/mount", "/tppa", "/filterwheel" };
            var connectionTasks = channelsToConnect.Select(channel => ConnectToChannelAsync(channel, cancellationToken)).ToArray();
            
            var results = await Task.WhenAll(connectionTasks);
            
            if (results.All(r => r.IsSuccessful))
            {
                _logger.LogInformation("Successfully connected to all NINA WebSocket channels");
                _reconnectAttempts = 0;
                ConnectionStateChanged?.Invoke(this, true);
                return Result<bool>.Success(true);
            }
            else
            {
                var failures = results.Where(r => !r.IsSuccessful).Select(r => r.Error?.Message).ToArray();
                var combinedError = string.Join("; ", failures);
                _logger.LogError("Failed to connect to some NINA WebSocket channels: {Errors}", combinedError);
                return Result<bool>.Failure(new InvalidOperationException($"Channel connection failures: {combinedError}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to NINA WebSocket server");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task<Result<bool>> ConnectToChannelAsync(string channel, CancellationToken cancellationToken)
    {
        try
        {
            var webSocket = new ClientWebSocket();
            webSocket.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(_options.KeepAliveIntervalMs);

            // Use the exact AsyncAPI specification format: ws://localhost:1888/v2 + channel
            var uri = new Uri($"{_options.BaseUri}{channel}");
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectionTimeoutMs);

            _logger.LogDebug("Attempting to connect to NINA WebSocket channel: {Uri}", uri);
            await webSocket.ConnectAsync(uri, timeoutCts.Token);

            _channelConnections[channel] = webSocket;
            _connectionMetrics[channel] = new ConnectionMetrics();
            
            // Start receiving messages for this channel with memory optimization
            _channelReceiveTasks[channel] = ReceiveChannelMessagesAsync(channel, _cancellationTokenSource.Token);

            _logger.LogInformation("✅ Connected to NINA WebSocket channel: {Channel} at {Uri}", channel, uri);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to connect to NINA WebSocket channel: {Channel} - {Error}", channel, ex.Message);
            return Result<bool>.Failure(ex);
        }
    }

    public async Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure(new ObjectDisposedException(nameof(NinaWebSocketClient)));
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!IsConnected)
            {
                _logger.LogDebug("Already disconnected from NINA WebSocket server");
                return Result<bool>.Success(true);
            }

            _logger.LogInformation("Disconnecting from NINA WebSocket server");

            // Disconnect from all AsyncAPI channels
            var disconnectionTasks = _channelConnections.Keys.Select(channel => DisconnectFromChannelAsync(channel, cancellationToken)).ToArray();
            await Task.WhenAll(disconnectionTasks);

            // Wait for all receive tasks to complete
            var receiveTasks = _channelReceiveTasks.Values.ToArray();
            if (receiveTasks.Length > 0)
            {
                await Task.WhenAll(receiveTasks);
            }

            _channelConnections.Clear();
            _channelReceiveTasks.Clear();
            _connectionMetrics.Clear();

            _logger.LogInformation("Successfully disconnected from NINA WebSocket server");
            ConnectionStateChanged?.Invoke(this, false);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect from NINA WebSocket server");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task DisconnectFromChannelAsync(string channel, CancellationToken cancellationToken)
    {
        try
        {
            if (_channelConnections.TryRemove(channel, out var webSocket))
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", cancellationToken);
                }
                webSocket.Dispose();
                
                _logger.LogDebug("Disconnected from WebSocket channel: {Channel}", channel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting from channel: {Channel}", channel);
        }
    }

    // Enhanced message receiving with memory optimization using BufferManager
    private async Task ReceiveChannelMessagesAsync(string channel, CancellationToken cancellationToken)
    {
        using var buffer = _bufferManager.RentBuffer(_options.BufferSize);
        var metrics = _connectionMetrics[channel];

        try
        {
            if (!_channelConnections.TryGetValue(channel, out var webSocket))
            {
                _logger.LogWarning("WebSocket connection not found for channel: {Channel}", channel);
                return;
            }

            _logger.LogDebug("Starting message receive loop for channel: {Channel}", channel);

            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(buffer.Memory, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer.Buffer, 0, result.Count);
                        _logger.LogTrace("Received message on channel {Channel}: {Message}", channel, message);
                        
                        // Update metrics
                        metrics.MessagesReceived++;
                        metrics.BytesReceived += result.Count;
                        metrics.LastMessageTime = DateTime.UtcNow;
                        
                        // Process message asynchronously to avoid blocking the receive loop
                        _ = Task.Run(() => ProcessChannelMessageAsync(channel, message), CancellationToken.None);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket channel {Channel} closed by server", channel);
                        metrics.DisconnectionTime = DateTime.UtcNow;
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("WebSocket receive operation cancelled for channel: {Channel}", channel);
                    break;
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    _logger.LogWarning("WebSocket channel {Channel} closed prematurely, attempting reconnect", channel);
                    metrics.ReconnectionAttempts++;
                    await AttemptChannelReconnectAsync(channel);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving WebSocket message on channel: {Channel}", channel);
                    metrics.ErrorCount++;
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in WebSocket receive loop for channel: {Channel}", channel);
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            _logger.LogDebug("Message receive loop ended for channel: {Channel}", channel);
            if (_channelConnections.Values.All(ws => ws.State != WebSocketState.Open))
            {
                ConnectionStateChanged?.Invoke(this, false);
            }
        }
    }

    private Task ProcessChannelMessageAsync(string channel, string message)
    {
        try
        {
            _logger.LogTrace("Processing message from channel {Channel}: {Message}", channel, message);

            // Handle different NINA WebSocket message formats with enhanced error handling
            if (TryParseDirectEventMessage(message, out var eventType, out var eventData))
            {
                var eventArgs = new NinaEventArgs(eventType, eventData, null);
                EventReceived?.Invoke(this, eventArgs);
                _logger.LogDebug("Processed direct NINA event: {EventType} from channel {Channel}", eventType, channel);
                return Task.CompletedTask;
            }

            // Try standard NINA API response format
            var response = JsonSerializer.Deserialize<NinaWebSocketResponse>(message);
            if (response == null)
            {
                _logger.LogWarning("Failed to deserialize WebSocket message from channel {Channel}: {Message}", channel, message);
                return Task.CompletedTask;
            }

            var ninaEventType = NinaEventType.Unknown;
            object? ninaEventData = null;

            // Parse the response based on the event type
            if (response.Response is JsonElement responseElement)
            {
                if (responseElement.ValueKind == JsonValueKind.Object && responseElement.TryGetProperty("Event", out var eventProperty))
                {
                    var eventName = eventProperty.GetString();
                    ninaEventType = ParseEventType(eventName);
                    ninaEventData = ParseEventData(eventName, responseElement);
                }
                else if (responseElement.ValueKind == JsonValueKind.String)
                {
                    // Simple string response
                    ninaEventData = responseElement.GetString();
                }
            }

            var eventArgs2 = new NinaEventArgs(ninaEventType, ninaEventData, response);
            EventReceived?.Invoke(this, eventArgs2);

            _logger.LogDebug("Processed NINA event from channel {Channel}: {EventType}", channel, ninaEventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket message from channel {Channel}: {Message}", channel, message);
            ErrorOccurred?.Invoke(this, ex);
        }

        return Task.CompletedTask;
    }

    // Mount control methods with enhanced error handling
    public async Task<Result<bool>> MoveMountAxisAsync(MountDirection direction, double rate, CancellationToken cancellationToken = default)
    {
        try
        {
            var command = new MountAxisMoveCommand
            {
                Direction = direction.ToString().ToLowerInvariant(),
                Rate = rate
            };

            var result = await SendToChannelAsync("/mount", JsonSerializer.Serialize(command), cancellationToken);
            if (result.IsSuccessful)
            {
                _logger.LogDebug("Mount axis move command sent - Direction: {Direction}, Rate: {Rate}", direction, rate);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send mount axis move command");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
    }

    public Task<Result<bool>> StopMountMovementAsync(CancellationToken cancellationToken = default)
    {
        // Mount movement stops automatically after 2 seconds without commands
        _logger.LogDebug("Mount movement will stop automatically after 2 seconds of inactivity");
        return Task.FromResult(Result<bool>.Success(true));
    }

    // TPPA methods with enhanced error handling
    public async Task<Result<bool>> StartTppaAlignmentAsync(TppaCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            command = command with { Action = "start-alignment" };
            var result = await SendToChannelAsync("/tppa", JsonSerializer.Serialize(command), cancellationToken);
            if (result.IsSuccessful)
            {
                _logger.LogInformation("TPPA alignment started with command: {Command}", JsonSerializer.Serialize(command));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start TPPA alignment");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
    }

    public async Task<Result<bool>> StopTppaAlignmentAsync(CancellationToken cancellationToken = default)
    {
        return await SendTppaCommandAsync("stop-alignment", cancellationToken);
    }

    public async Task<Result<bool>> PauseTppaAlignmentAsync(CancellationToken cancellationToken = default)
    {
        return await SendTppaCommandAsync("pause-alignment", cancellationToken);
    }

    public async Task<Result<bool>> ResumeTppaAlignmentAsync(CancellationToken cancellationToken = default)
    {
        return await SendTppaCommandAsync("resume-alignment", cancellationToken);
    }

    // Filter wheel methods
    public async Task<Result<bool>> GetTargetFilterAsync(CancellationToken cancellationToken = default)
    {
        return await SendToChannelAsync("/filterwheel", "get-target-filter", cancellationToken);
    }

    public async Task<Result<bool>> SignalFilterChangedAsync(CancellationToken cancellationToken = default)
    {
        return await SendToChannelAsync("/filterwheel", "filter-changed", cancellationToken);
    }

    // Enhanced test message with detailed diagnostics
    public async Task<Result<bool>> SendTestMessageAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending test message to NINA WebSocket");
        
        try
        {
            var channels = new[] { "/socket", "/mount", "/tppa", "/filterwheel" };
            var testResults = new List<Result<bool>>();
            
            foreach (var channel in channels)
            {
                try
                {
                    var result = await SendToChannelAsync(channel, "ping", cancellationToken);
                    testResults.Add(result);
                    
                    if (result.IsSuccessful)
                    {
                        _logger.LogInformation("✅ Test message sent successfully to channel {Channel}", channel);
                    }
                    else
                    {
                        _logger.LogWarning("❌ Failed to send test message to channel {Channel}: {Error}", channel, result.Error?.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error sending test message to channel {Channel}", channel);
                    testResults.Add(Result<bool>.Failure(ex));
                }
            }
            
            var successfulTests = testResults.Count(r => r.IsSuccessful);
            _logger.LogInformation("🎯 WebSocket test complete: {Successful}/{Total} channels responded", successfulTests, testResults.Count);
            
            return successfulTests > 0 ? Result<bool>.Success(true) : Result<bool>.Failure(new InvalidOperationException("No channels responded to test messages"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebSocket test");
            return Result<bool>.Failure(ex);
        }
    }

    // Enhanced discovery with detailed connection metrics
    public async Task<Result<string>> DiscoverWebSocketEndpointAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔍 Discovering available NINA WebSocket endpoints...");
        
        var baseUrl = _options.BaseUri.Replace("ws://", "").Replace("wss://", "");
        var parts = baseUrl.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1].Split('/')[0] : "1888";
        
        var channelsToTest = new[] { "/socket", "/mount", "/tppa", "/filterwheel" };
        var workingEndpoints = new List<string>();
        
        foreach (var channel in channelsToTest)
        {
            var endpoint = $"ws://{host}:{port}/v2{channel}";
            
            try
            {
                using var testSocket = new ClientWebSocket();
                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(2000);
                
                _logger.LogDebug("Testing WebSocket endpoint: {Endpoint}", endpoint);
                await testSocket.ConnectAsync(new Uri(endpoint), timeoutCts.Token);
                
                workingEndpoints.Add(endpoint);
                _logger.LogInformation("✅ Working WebSocket endpoint found: {Endpoint}", endpoint);
                
                await testSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Discovery test", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("❌ Endpoint {Endpoint} failed: {Error}", endpoint, ex.Message);
            }
        }
        
        if (workingEndpoints.Count > 0)
        {
            var result = string.Join(", ", workingEndpoints);
            _logger.LogInformation("🎯 Discovery complete. Working AsyncAPI channels: {Endpoints}", result);
            return Result<string>.Success(result);
        }
        else
        {
            var errorMsg = "No working AsyncAPI WebSocket channels found. Check that NINA is running with Advanced API plugin enabled.";
            _logger.LogError("❌ {ErrorMessage}", errorMsg);
            return Result<string>.Failure(new InvalidOperationException(errorMsg));
        }
    }

    /// <summary>
    /// Gets diagnostics information about all WebSocket connections
    /// </summary>
    /// <returns>WebSocket diagnostics</returns>
    public NinaWebSocketDiagnostics GetDiagnostics()
    {
        var bufferStats = _bufferManager.GetStatistics();
        var connectionStats = _connectionMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        
        return new NinaWebSocketDiagnostics
        {
            IsDisposed = _disposed,
            BaseUri = _options.BaseUri,
            IsConnected = IsConnected,
            ActiveChannels = _channelConnections.Count(kvp => kvp.Value.State == WebSocketState.Open),
            TotalChannels = _channelConnections.Count,
            ReconnectAttempts = _reconnectAttempts,
            CircuitBreakerEnabled = _options.EnableCircuitBreaker,
            CircuitBreakerState = _circuitBreaker?.State.ToString(),
            CircuitBreakerFailureCount = _circuitBreaker?.FailureCount ?? 0,
            BufferStatistics = bufferStats,
            ConnectionMetrics = connectionStats
        };
    }

    // Helper methods with enhanced error handling
    private async Task<Result<bool>> SendTppaCommandAsync(string action, CancellationToken cancellationToken)
    {
        try
        {
            var command = new TppaCommand { Action = action };
            var result = await SendToChannelAsync("/tppa", JsonSerializer.Serialize(command), cancellationToken);
            if (result.IsSuccessful)
            {
                _logger.LogInformation("TPPA command sent: {Action}", action);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send TPPA command: {Action}", action);
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
    }

    private async Task<Result<bool>> SendToChannelAsync(string channel, string message, CancellationToken cancellationToken)
    {
        if (!_channelConnections.TryGetValue(channel, out var webSocket))
        {
            return Result<bool>.Failure(new InvalidOperationException($"Not connected to channel: {channel}"));
        }

        if (webSocket.State != WebSocketState.Open)
        {
            return Result<bool>.Failure(new InvalidOperationException($"WebSocket channel {channel} is not open (state: {webSocket.State})"));
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var buffer = new ArraySegment<byte>(bytes);

            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
            
            // Update metrics
            if (_connectionMetrics.TryGetValue(channel, out var metrics))
            {
                metrics.MessagesSent++;
                metrics.BytesSent += bytes.Length;
            }
            
            _logger.LogTrace("Message sent to NINA WebSocket channel {Channel}: {Message}", channel, message);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WebSocket message to channel {Channel}", channel);
            if (_connectionMetrics.TryGetValue(channel, out var metrics))
            {
                metrics.ErrorCount++;
            }
            return Result<bool>.Failure(ex);
        }
    }

    private async Task AttemptChannelReconnectAsync(string channel)
    {
        if (_reconnectAttempts >= _options.MaxReconnectAttempts)
        {
            _logger.LogError("Maximum reconnection attempts ({MaxAttempts}) reached for channel: {Channel}", _options.MaxReconnectAttempts, channel);
            return;
        }

        _reconnectAttempts++;
        _logger.LogInformation("Attempting to reconnect channel {Channel} (attempt {Attempt}/{MaxAttempts})", 
            channel, _reconnectAttempts, _options.MaxReconnectAttempts);

        await Task.Delay(_options.ReconnectDelayMs, _cancellationTokenSource.Token);

        var result = await ConnectToChannelAsync(channel, _cancellationTokenSource.Token);
        if (!result.IsSuccessful)
        {
            _logger.LogWarning("Reconnection attempt {Attempt} failed for channel {Channel}: {Error}", 
                _reconnectAttempts, channel, result.Error?.Message);
        }
        else
        {
            _logger.LogInformation("Successfully reconnected to channel {Channel}", channel);
        }
    }

    // Event parsing methods
    private bool TryParseDirectEventMessage(string message, out NinaEventType eventType, out object? eventData)
    {
        eventType = NinaEventType.Unknown;
        eventData = null;

        try
        {
            if (!message.StartsWith("{") && !message.StartsWith("["))
            {
                eventType = ParseEventType(message.Trim().Trim('"'));
                eventData = message;
                return eventType != NinaEventType.Unknown;
            }

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (root.TryGetProperty("Event", out var eventProperty))
            {
                var eventName = eventProperty.GetString();
                eventType = ParseEventType(eventName);
                eventData = ParseEventData(eventName, root);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to parse as direct event message: {Message}", message);
            return false;
        }
    }

    private static NinaEventType ParseEventType(string? eventName)
    {
        return eventName switch
        {
            "API-CAPTURE-FINISHED" => NinaEventType.ApiCaptureFinished,
            "AUTOFOCUS-FINISHED" => NinaEventType.AutofocusFinished,
            "CAMERA-CONNECTED" => NinaEventType.CameraConnected,
            "CAMERA-DISCONNECTED" => NinaEventType.CameraDisconnected,
            "CAMERA-DOWNLOAD-TIMEOUT" => NinaEventType.CameraDownloadTimeout,
            "DOME-CONNECTED" => NinaEventType.DomeConnected,
            "DOME-DISCONNECTED" => NinaEventType.DomeDisconnected,
            "DOME-SHUTTER-CLOSED" => NinaEventType.DomeShutterClosed,
            "DOME-SHUTTER-OPENED" => NinaEventType.DomeShutterOpened,
            "DOME-HOMED" => NinaEventType.DomeHomed,
            "DOME-PARKED" => NinaEventType.DomeParked,
            "DOME-STOPPED" => NinaEventType.DomeStopped,
            "DOME-SLEWED" => NinaEventType.DomeSlewed,
            "DOME-SYNCED" => NinaEventType.DomeSynced,
            "FILTERWHEEL-CONNECTED" => NinaEventType.FilterWheelConnected,
            "FILTERWHEEL-DISCONNECTED" => NinaEventType.FilterWheelDisconnected,
            "FILTERWHEEL-CHANGED" => NinaEventType.FilterWheelChanged,
            "FLAT-CONNECTED" => NinaEventType.FlatConnected,
            "FLAT-DISCONNECTED" => NinaEventType.FlatDisconnected,
            "FLAT-LIGHT-TOGGLED" => NinaEventType.FlatLightToggled,
            "FLAT-COVER-OPENED" => NinaEventType.FlatCoverOpened,
            "FLAT-COVER-CLOSED" => NinaEventType.FlatCoverClosed,
            "FLAT-BRIGHTNESS-CHANGED" => NinaEventType.FlatBrightnessChanged,
            "FOCUSER-CONNECTED" => NinaEventType.FocuserConnected,
            "FOCUSER-DISCONNECTED" => NinaEventType.FocuserDisconnected,
            "FOCUSER-USER-FOCUSED" => NinaEventType.FocuserUserFocused,
            "GUIDER-CONNECTED" => NinaEventType.GuiderConnected,
            "GUIDER-DISCONNECTED" => NinaEventType.GuiderDisconnected,
            "GUIDER-START" => NinaEventType.GuiderStart,
            "GUIDER-STOP" => NinaEventType.GuiderStop,
            "GUIDER-DITHER" => NinaEventType.GuiderDither,
            "MOUNT-CONNECTED" => NinaEventType.MountConnected,
            "MOUNT-DISCONNECTED" => NinaEventType.MountDisconnected,
            "MOUNT-BEFORE-FLIP" => NinaEventType.MountBeforeFlip,
            "MOUNT-AFTER-FLIP" => NinaEventType.MountAfterFlip,
            "MOUNT-HOMED" => NinaEventType.MountHomed,
            "MOUNT-PARKED" => NinaEventType.MountParked,
            "MOUNT-UNPARKED" => NinaEventType.MountUnparked,
            "MOUNT-CENTER" => NinaEventType.MountCenter,
            "PROFILE-ADDED" => NinaEventType.ProfileAdded,
            "PROFILE-CHANGED" => NinaEventType.ProfileChanged,
            "PROFILE-REMOVED" => NinaEventType.ProfileRemoved,
            "ROTATOR-CONNECTED" => NinaEventType.RotatorConnected,
            "ROTATOR-DISCONNECTED" => NinaEventType.RotatorDisconnected,
            "ROTATOR-SYNCED" => NinaEventType.RotatorSynced,
            "ROTATOR-MOVED" => NinaEventType.RotatorMoved,
            "ROTATOR-MOVED-MECHANICAL" => NinaEventType.RotatorMovedMechanical,
            "SAFETY-CONNECTED" => NinaEventType.SafetyConnected,
            "SAFETY-DISCONNECTED" => NinaEventType.SafetyDisconnected,
            "SAFETY-CHANGED" => NinaEventType.SafetyChanged,
            "SEQUENCE-STARTING" => NinaEventType.SequenceStarting,
            "SEQUENCE-FINISHED" => NinaEventType.SequenceFinished,
            "SEQUENCE-ENTITY-FAILED" => NinaEventType.SequenceEntityFailed,
            "SWITCH-CONNECTED" => NinaEventType.SwitchConnected,
            "SWITCH-DISCONNECTED" => NinaEventType.SwitchDisconnected,
            "WEATHER-CONNECTED" => NinaEventType.WeatherConnected,
            "WEATHER-DISCONNECTED" => NinaEventType.WeatherDisconnected,
            "ADV-SEQ-START" => NinaEventType.AdvSeqStart,
            "ADV-SEQ-STOP" => NinaEventType.AdvSeqStop,
            "ERROR-AF" => NinaEventType.ErrorAf,
            "ERROR-PLATESOLVE" => NinaEventType.ErrorPlatesolve,
            "IMAGE-SAVE" => NinaEventType.ImageSave,
            "STACK-UPDATED" => NinaEventType.StackUpdated,
            "TS-WAITSTART" => NinaEventType.TsWaitStart,
            "TS-NEWTARGETSTART" => NinaEventType.TsNewTargetStart,
            "TS-TARGETSTART" => NinaEventType.TsTargetStart,
            _ => NinaEventType.Unknown
        };
    }

    private static object? ParseEventData(string? eventName, JsonElement responseElement)
    {
        try
        {
            return eventName switch
            {
                "FILTERWHEEL-CHANGED" => JsonSerializer.Deserialize<FilterChangedResponse>(responseElement),
                "FLAT-BRIGHTNESS-CHANGED" => JsonSerializer.Deserialize<FlatBrightnessChangedResponse>(responseElement),
                "SAFETY-CHANGED" => JsonSerializer.Deserialize<SafetyChangedResponse>(responseElement),
                "STACK-UPDATED" => JsonSerializer.Deserialize<StackUpdatedResponse>(responseElement),
                "TS-NEWTARGETSTART" or "TS-TARGETSTART" => JsonSerializer.Deserialize<TargetStartResponse>(responseElement),
                "ROTATOR-MOVED" or "ROTATOR-MOVED-MECHANICAL" => JsonSerializer.Deserialize<RotatorMovedResponse>(responseElement),
                "SEQUENCE-ENTITY-FAILED" => JsonSerializer.Deserialize<SequenceEntityFailedResponse>(responseElement),
                "IMAGE-SAVE" => ParseImageSaveEvent(responseElement),
                _ => JsonSerializer.Deserialize<SimpleEventResponse>(responseElement)
            };
        }
        catch
        {
            return JsonSerializer.Deserialize<SimpleEventResponse>(responseElement);
        }
    }

    private static object? ParseImageSaveEvent(JsonElement responseElement)
    {
        try
        {
            if (responseElement.TryGetProperty("ImageStatistics", out var statsElement))
            {
                return JsonSerializer.Deserialize<ImageStatistics>(statsElement);
            }
        }
        catch
        {
            // Ignore parsing errors for image statistics
        }

        return JsonSerializer.Deserialize<SimpleEventResponse>(responseElement);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _cancellationTokenSource.Cancel();
            
            // Dispose all channel connections
            foreach (var webSocket in _channelConnections.Values)
            {
                webSocket?.Dispose();
            }
            _channelConnections.Clear();
            _channelReceiveTasks.Clear();
            _connectionMetrics.Clear();
            
            _connectionSemaphore.Dispose();
            _cancellationTokenSource.Dispose();
            _bufferManager?.Dispose();
            _circuitBreaker?.Dispose();

            _logger.LogDebug("NinaWebSocketClient disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during NinaWebSocketClient disposal");
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Connection metrics for WebSocket channels
/// </summary>
public class ConnectionMetrics
{
    public DateTime ConnectionTime { get; } = DateTime.UtcNow;
    public DateTime? DisconnectionTime { get; set; }
    public DateTime? LastMessageTime { get; set; }
    public long MessagesReceived { get; set; }
    public long MessagesSent { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public int ErrorCount { get; set; }
    public int ReconnectionAttempts { get; set; }
}

/// <summary>
/// Diagnostics information for the NINA WebSocket client
/// </summary>
public record NinaWebSocketDiagnostics
{
    public bool IsDisposed { get; init; }
    public string BaseUri { get; init; } = string.Empty;
    public bool IsConnected { get; init; }
    public int ActiveChannels { get; init; }
    public int TotalChannels { get; init; }
    public int ReconnectAttempts { get; init; }
    public bool CircuitBreakerEnabled { get; init; }
    public string? CircuitBreakerState { get; init; }
    public int CircuitBreakerFailureCount { get; init; }
    public BufferStatistics BufferStatistics { get; init; } = new();
    public Dictionary<string, ConnectionMetrics> ConnectionMetrics { get; init; } = new();
}
