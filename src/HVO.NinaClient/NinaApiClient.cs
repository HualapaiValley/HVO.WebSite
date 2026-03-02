using HVO;
using HVO.NinaClient.Models;
using HVO.Core.Results;
using HVO.NinaClient.Resilience;
using HVO.NinaClient.Infrastructure;
using HVO.NinaClient.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace HVO.NinaClient;

/// <summary>
/// NINA API client for astronomy equipment control and imaging operations with enhanced resilience
/// </summary>
public class NinaApiClient : INinaApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NinaApiClient> _logger;
    private readonly NinaApiClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CircuitBreaker? _circuitBreaker;
    private readonly BufferManager _bufferManager;
    private bool _disposed;

    public NinaApiClient(
        HttpClient httpClient,
        ILogger<NinaApiClient> logger,
        IOptions<NinaApiClientOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Validate configuration at startup
        _options.ValidateAndThrow();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        // Initialize circuit breaker if enabled in configuration
        // Circuit breaker prevents cascading failures by tracking failure patterns
        if (_options.EnableCircuitBreaker)
        {
            _circuitBreaker = new CircuitBreaker(
                _options.CircuitBreakerFailureThreshold,              // How many failures before opening (e.g., 5)
                TimeSpan.FromSeconds(_options.CircuitBreakerTimeoutSeconds), // How long to stay open (e.g., 30s)
                logger);                                              // Logger for circuit breaker state changes
                
            _logger.LogInformation("Circuit breaker enabled - FailureThreshold: {FailureThreshold}, Timeout: {Timeout}s",
                _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerTimeoutSeconds);
        }

        // Initialize buffer manager for memory optimization
        _bufferManager = new BufferManager();

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        
        // Add authentication headers if configured
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _options.ApiKey);
        }

        _logger.LogInformation("NINA API client configured - BaseUrl: {BaseUrl}, Timeout: {Timeout}s, CircuitBreaker: {CircuitBreakerEnabled}", 
            _options.BaseUrl, _options.TimeoutSeconds, _options.EnableCircuitBreaker);
    }

    #region Application Methods

    public async Task<Result<string>> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting NINA version");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/version", cancellationToken));
    }

    public async Task<Result<string>> GetApplicationStartTimeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting NINA application start time");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/application-start", cancellationToken));
    }

    public async Task<Result<string>> SwitchTabAsync(string tab, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Switching to application tab: {Tab}", tab);
        
        var endpoint = $"/v2/api/application/switch-tab?tab={Uri.EscapeDataString(tab)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    public async Task<Result<string>> GetCurrentTabAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting current application tab");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/application/get-tab", cancellationToken));
    }

    public async Task<Result<IReadOnlyList<string>>> GetInstalledPluginsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting list of installed plugins");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<string>>("/v2/api/application/plugins", cancellationToken));
    }

    public async Task<Result<IReadOnlyList<LogEntry>>> GetApplicationLogsAsync(int lineCount, string? level = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting application logs - LineCount: {LineCount}, Level: {Level}", lineCount, level);
        
        var queryParams = new List<string> { $"lineCount={lineCount}" };
        
        if (!string.IsNullOrEmpty(level))
        {
            queryParams.Add($"level={Uri.EscapeDataString(level)}");
        }
        
        var endpoint = "/v2/api/application/logs?" + string.Join("&", queryParams);
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<LogEntry>>(endpoint, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<EventEntry>>> GetEventHistoryAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting event history");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<EventEntry>>("/v2/api/event-history", cancellationToken));
    }

    public async Task<Result<string>> TakeScreenshotAsync(
        bool? resize = null,
        int? quality = null,
        string? size = null,
        double? scale = null,
        bool? stream = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Taking application screenshot - Resize: {Resize}, Quality: {Quality}, Size: {Size}, Scale: {Scale}, Stream: {Stream}", 
            resize, quality, size, scale, stream);

        var queryParams = new List<string>();
        
        if (resize.HasValue)
            queryParams.Add($"resize={resize.Value.ToString().ToLower()}");
            
        if (quality.HasValue)
            queryParams.Add($"quality={quality.Value}");
            
        if (!string.IsNullOrEmpty(size))
            queryParams.Add($"size={Uri.EscapeDataString(size)}");
            
        if (scale.HasValue)
            queryParams.Add($"scale={scale.Value}");
            
        if (stream.HasValue)
            queryParams.Add($"stream={stream.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/application/screenshot";
        if (queryParams.Count > 0)
        {
            endpoint += "?" + string.Join("&", queryParams);
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    #endregion

    #region Camera Equipment Methods

    /// <summary>
    /// Gets information about available cameras
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing camera information</returns>
    public async Task<Result<CameraInfo>> GetCameraInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting camera information");
        return await ExecuteWithResilienceAsync(() => GetAsync<CameraInfo>("/v2/api/equipment/camera/info", cancellationToken));
    }

    /// <summary>
    /// Gets list of available cameras
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available cameras</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetCameraDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available camera devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/camera/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new camera devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available cameras</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanCameraDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for camera devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/camera/rescan", cancellationToken));
    }

    /// <summary>
    /// Connects to a camera device
    /// </summary>
    /// <param name="deviceId">The ID of the camera device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectCameraAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to camera device: {DeviceId}", deviceId ?? "default");
        
        var endpoint = "/v2/api/equipment/camera/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnects the currently connected camera
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectCameraAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting camera");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/camera/disconnect", cancellationToken));
    }

    /// <summary>
    /// Sets the camera readout mode
    /// </summary>
    /// <param name="mode">The readout mode to set (0-based index)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the readout mode operation status</returns>
    public async Task<Result<string>> SetCameraReadoutModeAsync(int mode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting camera readout mode to {Mode}", mode);
        var endpoint = $"/v2/api/equipment/camera/set-readout?mode={mode}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Cools the camera to specified temperature
    /// </summary>
    /// <param name="temperature">Target temperature in degrees Celsius</param>
    /// <param name="minutes">Minimum duration to cool (-1 for default duration)</param>
    /// <param name="cancel">Whether to cancel the cooling process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the cooling operation status</returns>
    public async Task<Result<string>> CoolCameraAsync(double temperature, double minutes, bool? cancel = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cooling camera to {Temperature}°C for {Minutes} minutes, Cancel: {Cancel}", temperature, minutes, cancel);
        
        var queryParams = new List<string>
        {
            $"temperature={temperature}",
            $"minutes={minutes}"
        };

        if (cancel.HasValue)
            queryParams.Add($"cancel={cancel.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/equipment/camera/cool?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Warms the camera
    /// </summary>
    /// <param name="minutes">Minimum duration to warm (-1 for default duration)</param>
    /// <param name="cancel">Whether to cancel the warming process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the warming operation status</returns>
    public async Task<Result<string>> WarmCameraAsync(double minutes, bool? cancel = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Warming camera for {Minutes} minutes, Cancel: {Cancel}", minutes, cancel);
        
        var queryParams = new List<string> { $"minutes={minutes}" };

        if (cancel.HasValue)
            queryParams.Add($"cancel={cancel.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/equipment/camera/warm?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Sets the camera dew heater power
    /// </summary>
    /// <param name="power">Whether to turn the dew heater on or off</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the dew heater operation status</returns>
    public async Task<Result<string>> SetCameraDewHeaterAsync(bool power, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting camera dew heater power: {Power}", power);
        var endpoint = $"/v2/api/equipment/camera/dew-heater?power={power.ToString().ToLower()}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Sets the camera binning mode
    /// </summary>
    /// <param name="binning">The binning mode (e.g., "1x1", "2x2", "3x3")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the binning operation status</returns>
    public async Task<Result<string>> SetCameraBinningAsync(string binning, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting camera binning to {Binning}", binning);
        var endpoint = $"/v2/api/equipment/camera/set-binning?binning={Uri.EscapeDataString(binning)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Starts a camera capture with comprehensive parameters matching NINA API specification
    /// </summary>
    /// <param name="solve">Whether to solve the image</param>
    /// <param name="duration">The duration of the exposure in seconds</param>
    /// <param name="gain">The gain to use for the exposure</param>
    /// <param name="getResult">Whether to get the result</param>
    /// <param name="resize">Whether to resize the image</param>
    /// <param name="quality">The quality of the image (1-100, -1 for PNG)</param>
    /// <param name="size">The size of the image ([width]x[height])</param>
    /// <param name="scale">The scale of the image</param>
    /// <param name="stream">Stream the image to the client</param>
    /// <param name="omitImage">Omit the image from the response</param>
    /// <param name="waitForResult">Wait for the capture to finish and return the result</param>
    /// <param name="save">Save the image to disk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing either a status message or capture data</returns>
    public async Task<Result<CaptureResponseOrString>> CaptureAsync(
        bool? solve = null,
        double? duration = null,
        int? gain = null,
        int? getResult = null,
        bool? resize = null,
        int? quality = null,
        string? size = null,
        double? scale = null,
        bool? stream = null,
        bool? omitImage = null,
        bool? waitForResult = null,
        bool? save = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting camera capture - Duration: {Duration}s, Gain: {Gain}, WaitForResult: {WaitForResult}", 
            duration, gain, waitForResult);

        var queryParams = new List<string>();

        if (solve.HasValue)
            queryParams.Add($"solve={solve.Value.ToString().ToLower()}");
        
        if (duration.HasValue)
            queryParams.Add($"duration={duration.Value}");
        
        if (gain.HasValue)
            queryParams.Add($"gain={gain.Value}");
            
        if (getResult.HasValue)
            queryParams.Add($"getResult={getResult.Value.ToString().ToLower()}");
        
        if (resize.HasValue)
            queryParams.Add($"resize={resize.Value.ToString().ToLower()}");
        
        if (quality.HasValue)
            queryParams.Add($"quality={quality.Value}");
        
        if (!string.IsNullOrEmpty(size))
            queryParams.Add($"size={Uri.EscapeDataString(size)}");
        
        if (scale.HasValue)
            queryParams.Add($"scale={scale.Value}");
        
        if (stream.HasValue)
            queryParams.Add($"stream={stream.Value.ToString().ToLower()}");
        
        if (omitImage.HasValue)
            queryParams.Add($"omitImage={omitImage.Value.ToString().ToLower()}");
        
        if (waitForResult.HasValue)
            queryParams.Add($"waitForResult={waitForResult.Value.ToString().ToLower()}");
        
        if (save.HasValue)
            queryParams.Add($"save={save.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/equipment/camera/capture";
        if (queryParams.Count > 0)
        {
            endpoint += "?" + string.Join("&", queryParams);
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<CaptureResponseOrString>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Aborts the current camera exposure
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the abort status message</returns>
    public async Task<Result<string>> AbortCameraExposureAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Aborting camera exposure");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/camera/abort-exposure", cancellationToken));
    }

    /// <summary>
    /// Gets the last captured image statistics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing image statistics</returns>
    public async Task<Result<ImageStatistics>> GetCameraStatisticsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting camera statistics");
        return await ExecuteWithResilienceAsync(() => GetAsync<ImageStatistics>("/v2/api/equipment/camera/capture/statistics", cancellationToken));
    }

    #endregion

    #region Dome Equipment Methods

    /// <summary>
    /// Gets information about the dome
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing dome information</returns>
    public async Task<Result<DomeInfo>> GetDomeInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting dome information");
        return await ExecuteWithResilienceAsync(() => GetAsync<DomeInfo>("/v2/api/equipment/dome/info", cancellationToken));
    }

    /// <summary>
    /// Gets list of available dome devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available dome devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetDomeDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available dome devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/dome/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new dome devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available dome devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanDomeDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for dome devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/dome/rescan", cancellationToken));
    }

    /// <summary>
    /// Connects to a dome device
    /// </summary>
    /// <param name="deviceId">The ID of the dome device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectDomeAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to dome device: {DeviceId}", deviceId ?? "default");
        
        var endpoint = "/v2/api/equipment/dome/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnects the currently connected dome
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectDomeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting dome");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/disconnect", cancellationToken));
    }

    /// <summary>
    /// Opens the dome shutter
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the shutter operation status</returns>
    public async Task<Result<string>> OpenDomeShutterAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Opening dome shutter");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/open", cancellationToken));
    }

    /// <summary>
    /// Closes the dome shutter
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the shutter operation status</returns>
    public async Task<Result<string>> CloseDomeShutterAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Closing dome shutter");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/close", cancellationToken));
    }

    /// <summary>
    /// Stops dome movement
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop operation status</returns>
    public async Task<Result<string>> StopDomeMovementAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping dome movement");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/stop", cancellationToken));
    }

    /// <summary>
    /// Sets dome follow mode to enable or disable the dome following the telescope
    /// </summary>
    /// <param name="enabled">Whether to enable or disable dome follow</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the follow operation status</returns>
    public async Task<Result<string>> SetDomeFollowAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting dome follow: {Enabled}", enabled);
        var endpoint = $"/v2/api/equipment/dome/set-follow?enabled={enabled.ToString().ToLower()}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Syncs dome to telescope coordinates
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the sync operation status</returns>
    public async Task<Result<string>> SyncDomeToTelescopeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Syncing dome to telescope");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/sync", cancellationToken));
    }

    /// <summary>
    /// Slews dome to specified azimuth
    /// </summary>
    /// <param name="azimuth">Azimuth in degrees</param>
    /// <param name="waitToFinish">Whether to wait until slew is finished</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the slew operation status</returns>
    public async Task<Result<string>> SlewDomeAsync(double azimuth, bool? waitToFinish = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Slewing dome to azimuth: {Azimuth}°, WaitToFinish: {WaitToFinish}", azimuth, waitToFinish);
        
        var queryParams = new List<string> { $"azimuth={azimuth}" };
        
        if (waitToFinish.HasValue)
            queryParams.Add($"waitToFinish={waitToFinish.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/equipment/dome/slew?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Sets the current dome position as park position (if supported)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the park position set operation status</returns>
    public async Task<Result<string>> SetDomeParkPositionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting dome park position");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/set-park-position", cancellationToken));
    }

    /// <summary>
    /// Parks the dome
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the park operation status</returns>
    public async Task<Result<string>> ParkDomeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parking dome");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/park", cancellationToken));
    }

    /// <summary>
    /// Homes the dome
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the home operation status</returns>
    public async Task<Result<string>> HomeDomeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Homing dome");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/dome/home", cancellationToken));
    }

    #endregion

    #region Filter Wheel Equipment Methods

    /// <summary>
    /// Gets information about the filter wheel
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing filter wheel information</returns>
    public async Task<Result<FilterWheelInfo>> GetFilterWheelInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting filter wheel information");
        return await ExecuteWithResilienceAsync(() => GetAsync<FilterWheelInfo>("/v2/api/equipment/filterwheel/info", cancellationToken));
    }

    /// <summary>
    /// Gets list of available filter wheel devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available filter wheel devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetFilterWheelDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available filter wheel devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/filterwheel/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new filter wheel devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available filter wheel devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanFilterWheelDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for filter wheel devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/filterwheel/rescan", cancellationToken));
    }

    /// <summary>
    /// Connects to a filter wheel device
    /// </summary>
    /// <param name="deviceId">The ID of the filter wheel device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectFilterWheelAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to filter wheel device: {DeviceId}", deviceId ?? "default");
        
        var endpoint = "/v2/api/equipment/filterwheel/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnects the currently connected filter wheel
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectFilterWheelAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting filter wheel");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/filterwheel/disconnect", cancellationToken));
    }

    /// <summary>
    /// Changes the filter wheel to the specified filter
    /// </summary>
    /// <param name="filterId">The ID of the filter to change to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the filter change operation status</returns>
    public async Task<Result<string>> ChangeFilterAsync(int filterId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Changing filter to filter ID: {FilterId}", filterId);
        var endpoint = $"/v2/api/equipment/filterwheel/change-filter?filterId={filterId}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Gets information about a specific filter
    /// </summary>
    /// <param name="filterId">The ID of the filter to get information for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing filter information</returns>
    public async Task<Result<FilterInfo>> GetFilterInfoAsync(int filterId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting filter information for filter ID: {FilterId}", filterId);
        var endpoint = $"/v2/api/equipment/filterwheel/filter-info?filterId={filterId}";
        return await ExecuteWithResilienceAsync(() => GetAsync<FilterInfo>(endpoint, cancellationToken));
    }

    #endregion

    #region Flat Device Equipment Methods

    /// <summary>
    /// Gets information about the flat device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing flat device information</returns>
    public async Task<Result<FlatDeviceInfo>> GetFlatDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting flat device information");
        return await ExecuteWithResilienceAsync(() => GetAsync<FlatDeviceInfo>("/v2/api/equipment/flatdevice/info", cancellationToken));
    }

    /// <summary>
    /// Gets list of available flat device devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available flat device devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetFlatDeviceDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available flat device devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/flatdevice/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new flat device devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available flat device devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanFlatDeviceDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for flat device devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/flatdevice/rescan", cancellationToken));
    }

    /// <summary>
    /// Connects to a flat device device
    /// </summary>
    /// <param name="deviceId">The ID of the flat device device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectFlatDeviceAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to flat device device: {DeviceId}", deviceId ?? "default");
        
        var endpoint = "/v2/api/equipment/flatdevice/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnects the currently connected flat device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectFlatDeviceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting flat device");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/flatdevice/disconnect", cancellationToken));
    }

    /// <summary>
    /// Sets the flat device light on or off
    /// </summary>
    /// <param name="on">Whether to turn the light on or off</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the light operation status</returns>
    public async Task<Result<string>> SetFlatDeviceLightAsync(bool on, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting flat device light: {On}", on);
        var endpoint = $"/v2/api/equipment/flatdevice/set-light?on={on.ToString().ToLower()}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Sets the flat device cover position
    /// </summary>
    /// <param name="closed">Whether to close or open the cover</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the cover operation status</returns>
    public async Task<Result<string>> SetFlatDeviceCoverAsync(bool closed, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting flat device cover closed: {Closed}", closed);
        var endpoint = $"/v2/api/equipment/flatdevice/set-cover?closed={closed.ToString().ToLower()}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Sets the flat device brightness
    /// </summary>
    /// <param name="brightness">The brightness value (0-100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the brightness operation status</returns>
    public async Task<Result<string>> SetFlatDeviceBrightnessAsync(int brightness, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting flat device brightness to: {Brightness}", brightness);
        var endpoint = $"/v2/api/equipment/flatdevice/set-brightness?brightness={brightness}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    #endregion

    #region Focuser Equipment Methods

    /// <summary>
    /// Gets information about the focuser
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing focuser information</returns>
    public async Task<Result<FocuserInfo>> GetFocuserInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting focuser information");
        return await ExecuteWithResilienceAsync(() => GetAsync<FocuserInfo>("/v2/api/equipment/focuser/info", cancellationToken));
    }

    /// <summary>
    /// Gets list of available focuser devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available focuser devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetFocuserDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available focuser devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/focuser/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new focuser devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available focuser devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanFocuserDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for focuser devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/focuser/rescan", cancellationToken));
    }

    /// <summary>
    /// Connects to a focuser device
    /// </summary>
    /// <param name="deviceId">The ID of the focuser device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectFocuserAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to focuser device: {DeviceId}", deviceId ?? "default");
        
        var endpoint = "/v2/api/equipment/focuser/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnects the currently connected focuser
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectFocuserAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting focuser");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/focuser/disconnect", cancellationToken));
    }

    /// <summary>
    /// Moves the focuser to the specified position
    /// </summary>
    /// <param name="position">The position to move the focuser to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the move operation status</returns>
    public async Task<Result<string>> MoveFocuserAsync(int position, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Moving focuser to position: {Position}", position);
        var endpoint = $"/v2/api/equipment/focuser/move?position={position}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Starts an autofocus operation or cancels a running autofocus
    /// </summary>
    /// <param name="cancel">Whether to cancel a running autofocus operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the autofocus operation status</returns>
    public async Task<Result<string>> AutoFocusAsync(bool? cancel = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting autofocus operation - Cancel: {Cancel}", cancel);
        
        var endpoint = "/v2/api/equipment/focuser/auto-focus";
        if (cancel.HasValue)
        {
            endpoint += $"?cancel={cancel.Value.ToString().ToLower()}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Gets the last autofocus result
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the last autofocus information</returns>
    public async Task<Result<FocuserLastAF>> GetLastAutoFocusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting last autofocus result");
        return await ExecuteWithResilienceAsync(() => GetAsync<FocuserLastAF>("/v2/api/equipment/focuser/last-af", cancellationToken));
    }

    #endregion

    #region Flat Capture Methods

    /// <summary>
    /// Starts capturing sky flats. This requires the camera and mount to be connected.
    /// Any omitted parameter will default to the instruction default.
    /// </summary>
    /// <param name="count">The number of flats to capture (required)</param>
    /// <param name="minExposure">The minimum exposure time to use for the flats, in seconds</param>
    /// <param name="maxExposure">The maximum exposure time to use for the flats, in seconds</param>
    /// <param name="histogramMean">The mean to use for the histogram (0-1)</param>
    /// <param name="meanTolerance">The tolerance to use for the histogram (0-1)</param>
    /// <param name="dither">Whether to dither the flats</param>
    /// <param name="filterId">The filter to use for the flats. The current filter will be used if not specified</param>
    /// <param name="binning">The binning to use for the flats (e.g., "2x2")</param>
    /// <param name="gain">The gain to use for the flats. The camera gain will be used if not specified</param>
    /// <param name="offset">The offset to use for the flats. The camera offset will be used if not specified</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the sky flat capture operation status</returns>
    public async Task<Result<string>> CaptureSkyFlatsAsync(
        int count,
        double? minExposure = null,
        double? maxExposure = null,
        double? histogramMean = null,
        double? meanTolerance = null,
        bool? dither = null,
        int? filterId = null,
        string? binning = null,
        int? gain = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sky flat capture - Count: {Count}, MinExposure: {MinExposure}s, MaxExposure: {MaxExposure}s", 
            count, minExposure, maxExposure);

        var queryParams = new List<string> { $"count={count}" };

        if (minExposure.HasValue)
            queryParams.Add($"minExposure={minExposure.Value}");

        if (maxExposure.HasValue)
            queryParams.Add($"maxExposure={maxExposure.Value}");

        if (histogramMean.HasValue)
            queryParams.Add($"histogramMean={histogramMean.Value}");

        if (meanTolerance.HasValue)
            queryParams.Add($"meanTolerance={meanTolerance.Value}");

        if (dither.HasValue)
            queryParams.Add($"dither={dither.Value.ToString().ToLower()}");

        if (filterId.HasValue)
            queryParams.Add($"filterId={filterId.Value}");

        if (!string.IsNullOrEmpty(binning))
            queryParams.Add($"binning={Uri.EscapeDataString(binning)}");

        if (gain.HasValue)
            queryParams.Add($"gain={gain.Value}");

        if (offset.HasValue)
            queryParams.Add($"offset={offset.Value}");

        var endpoint = "/v2/api/flats/skyflat?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Starts capturing auto brightness flats. This requires the camera to be connected.
    /// NINA will pick the best flat panel brightness for a fixed exposure time.
    /// Any omitted parameter will default to the instruction default.
    /// </summary>
    /// <param name="count">The number of flats to capture (required)</param>
    /// <param name="exposureTime">The exposure time to use for the flats, in seconds (required)</param>
    /// <param name="minBrightness">The minimum flat panel brightness to use for the flats (0-99)</param>
    /// <param name="maxBrightness">The maximum flat panel brightness to use for the flats (1-100)</param>
    /// <param name="histogramMean">The mean to use for the histogram (0-1)</param>
    /// <param name="meanTolerance">The tolerance to use for the histogram (0-1)</param>
    /// <param name="filterId">The filter to use for the flats. The current filter will be used if not specified</param>
    /// <param name="binning">The binning to use for the flats (e.g., "2x2")</param>
    /// <param name="gain">The gain to use for the flats. The camera gain will be used if not specified</param>
    /// <param name="offset">The offset to use for the flats. The camera offset will be used if not specified</param>
    /// <param name="keepClosed">Whether to keep the flat panel closed after taking the flats</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the auto brightness flat capture operation status</returns>
    public async Task<Result<string>> CaptureAutoBrightnessFlatsAsync(
        int count,
        double exposureTime,
        int? minBrightness = null,
        int? maxBrightness = null,
        double? histogramMean = null,
        double? meanTolerance = null,
        int? filterId = null,
        string? binning = null,
        int? gain = null,
        int? offset = null,
        bool? keepClosed = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting auto brightness flat capture - Count: {Count}, ExposureTime: {ExposureTime}s", 
            count, exposureTime);

        var queryParams = new List<string> 
        { 
            $"count={count}",
            $"exposureTime={exposureTime}"
        };

        if (minBrightness.HasValue)
            queryParams.Add($"minBrightness={minBrightness.Value}");

        if (maxBrightness.HasValue)
            queryParams.Add($"maxBrightness={maxBrightness.Value}");

        if (histogramMean.HasValue)
            queryParams.Add($"histogramMean={histogramMean.Value}");

        if (meanTolerance.HasValue)
            queryParams.Add($"meanTolerance={meanTolerance.Value}");

        if (filterId.HasValue)
            queryParams.Add($"filterId={filterId.Value}");

        if (!string.IsNullOrEmpty(binning))
            queryParams.Add($"binning={Uri.EscapeDataString(binning)}");

        if (gain.HasValue)
            queryParams.Add($"gain={gain.Value}");

        if (offset.HasValue)
            queryParams.Add($"offset={offset.Value}");

        if (keepClosed.HasValue)
            queryParams.Add($"keepClosed={keepClosed.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/flats/auto-brightness?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Starts capturing auto exposure flats. This requires the camera to be connected.
    /// NINA will pick the best exposure time for a fixed flat panel brightness.
    /// Any omitted parameter will default to the instruction default.
    /// </summary>
    /// <param name="count">The number of flats to capture (required)</param>
    /// <param name="brightness">The flat panel brightness (0-100) (required)</param>
    /// <param name="minExposure">The minimum exposure time to use for the flats, in seconds</param>
    /// <param name="maxExposure">The maximum exposure time to use for the flats, in seconds</param>
    /// <param name="histogramMean">The mean to use for the histogram (0-1)</param>
    /// <param name="meanTolerance">The tolerance to use for the histogram (0-1)</param>
    /// <param name="filterId">The filter to use for the flats. The current filter will be used if not specified</param>
    /// <param name="binning">The binning to use for the flats (e.g., "2x2")</param>
    /// <param name="gain">The gain to use for the flats. The camera gain will be used if not specified</param>
    /// <param name="offset">The offset to use for the flats. The camera offset will be used if not specified</param>
    /// <param name="keepClosed">Whether to keep the flat panel closed after taking the flats</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the auto exposure flat capture operation status</returns>
    public async Task<Result<string>> CaptureAutoExposureFlatsAsync(
        int count,
        double brightness,
        double? minExposure = null,
        double? maxExposure = null,
        double? histogramMean = null,
        double? meanTolerance = null,
        int? filterId = null,
        string? binning = null,
        int? gain = null,
        int? offset = null,
        bool? keepClosed = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting auto exposure flat capture - Count: {Count}, Brightness: {Brightness}", 
            count, brightness);

        var queryParams = new List<string> 
        { 
            $"count={count}",
            $"brightness={brightness}"
        };

        if (minExposure.HasValue)
            queryParams.Add($"minExposure={minExposure.Value}");

        if (maxExposure.HasValue)
            queryParams.Add($"maxExposure={maxExposure.Value}");

        if (histogramMean.HasValue)
            queryParams.Add($"histogramMean={histogramMean.Value}");

        if (meanTolerance.HasValue)
            queryParams.Add($"meanTolerance={meanTolerance.Value}");

        if (filterId.HasValue)
            queryParams.Add($"filterId={filterId.Value}");

        if (!string.IsNullOrEmpty(binning))
            queryParams.Add($"binning={Uri.EscapeDataString(binning)}");

        if (gain.HasValue)
            queryParams.Add($"gain={gain.Value}");

        if (offset.HasValue)
            queryParams.Add($"offset={offset.Value}");

        if (keepClosed.HasValue)
            queryParams.Add($"keepClosed={keepClosed.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/flats/auto-exposure?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Starts capturing darkflats based on previous training done in NINA. This requires the camera to be connected.
    /// Any omitted parameter will default to the instruction default.
    /// </summary>
    /// <param name="count">The number of darkflats to capture (required)</param>
    /// <param name="filterId">The filter to use for the darkflats. The current filter will be used if not specified</param>
    /// <param name="binning">The binning to use for the darkflats (e.g., "2x2")</param>
    /// <param name="gain">The gain to use for the darkflats. The camera gain will be used if not specified</param>
    /// <param name="offset">The offset to use for the darkflats. The camera offset will be used if not specified</param>
    /// <param name="keepClosed">Whether to keep the flat panel closed after taking the darkflats</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the trained dark flat capture operation status</returns>
    public async Task<Result<string>> CaptureTrainedDarkFlatsAsync(
        int count,
        int? filterId = null,
        string? binning = null,
        int? gain = null,
        int? offset = null,
        bool? keepClosed = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting trained dark flat capture - Count: {Count}", count);

        var queryParams = new List<string> { $"count={count}" };

        if (filterId.HasValue)
            queryParams.Add($"filterId={filterId.Value}");

        if (!string.IsNullOrEmpty(binning))
            queryParams.Add($"binning={Uri.EscapeDataString(binning)}");

        if (gain.HasValue)
            queryParams.Add($"gain={gain.Value}");

        if (offset.HasValue)
            queryParams.Add($"offset={offset.Value}");

        if (keepClosed.HasValue)
            queryParams.Add($"keepClosed={keepClosed.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/flats/trained-dark-flat?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Starts capturing flats based on previous training done in NINA. This requires the camera to be connected.
    /// Any omitted parameter will default to the instruction default.
    /// </summary>
    /// <param name="count">The number of flats to capture (required)</param>
    /// <param name="filterId">The filter to use for the flats. The current filter will be used if not specified</param>
    /// <param name="binning">The binning to use for the flats (e.g., "2x2")</param>
    /// <param name="gain">The gain to use for the flats. The camera gain will be used if not specified</param>
    /// <param name="offset">The offset to use for the flats. The camera offset will be used if not specified</param>
    /// <param name="keepClosed">Whether to keep the flat panel closed after taking the flats</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the trained flat capture operation status</returns>
    public async Task<Result<string>> CaptureTrainedFlatsAsync(
        int count,
        int? filterId = null,
        string? binning = null,
        int? gain = null,
        int? offset = null,
        bool? keepClosed = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting trained flat capture - Count: {Count}", count);

        var queryParams = new List<string> { $"count={count}" };

        if (filterId.HasValue)
            queryParams.Add($"filterId={filterId.Value}");

        if (!string.IsNullOrEmpty(binning))
            queryParams.Add($"binning={Uri.EscapeDataString(binning)}");

        if (gain.HasValue)
            queryParams.Add($"gain={gain.Value}");

        if (offset.HasValue)
            queryParams.Add($"offset={offset.Value}");

        if (keepClosed.HasValue)
            queryParams.Add($"keepClosed={keepClosed.Value.ToString().ToLower()}");

        var endpoint = "/v2/api/flats/trained-flat?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Returns the current status of the flat taking process (Running or Finished)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the flat capture status information</returns>
    public async Task<Result<FlatCaptureStatus>> GetFlatCaptureStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting flat capture status");
        return await ExecuteWithResilienceAsync(() => GetAsync<FlatCaptureStatus>("/v2/api/flats/status", cancellationToken));
    }

    /// <summary>
    /// Stops a running flat taking process
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop operation status</returns>
    public async Task<Result<string>> StopFlatCaptureAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping flat capture process");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/flats/stop", cancellationToken));
    }

    #endregion

    #region Framing Assistant Methods

    /// <summary>
    /// Gets information about the framing assistant
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing framing assistant information</returns>
    public async Task<Result<FramingAssistantInfo>> GetFramingAssistantInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting framing assistant information");
        return await ExecuteWithResilienceAsync(() => GetAsync<FramingAssistantInfo>("/v2/api/framing/info", cancellationToken));
    }

    /// <summary>
    /// Sets the framing assistant image source
    /// </summary>
    /// <param name="source">The image source to set (e.g., NASA, SKYSERVER, STSCI, ESO)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set source operation status</returns>
    public async Task<Result<string>> SetFramingAssistantSourceAsync(string source, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting framing assistant source: {Source}", source);
        var endpoint = $"/v2/api/framing/set-source?source={Uri.EscapeDataString(source)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Sets the framing assistant coordinates
    /// </summary>
    /// <param name="rightAscension">Right ascension in degrees</param>
    /// <param name="declination">Declination in degrees</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set coordinates operation status</returns>
    public async Task<Result<string>> SetFramingAssistantCoordinatesAsync(double rightAscension, double declination, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting framing assistant coordinates - RA: {RightAscension}°, Dec: {Declination}°", rightAscension, declination);
        var endpoint = $"/v2/api/framing/set-coordinates?rightAscension={rightAscension}&declination={declination}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Slews the mount to the current framing assistant coordinates
    /// </summary>
    /// <param name="option">Optional slew option (e.g., "Center", "Rotate")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the slew operation status</returns>
    public async Task<Result<string>> SlewFramingAssistantAsync(string? option = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Slewing mount using framing assistant - Option: {Option}", option ?? "default");
        
        var endpoint = "/v2/api/framing/slew";
        if (!string.IsNullOrEmpty(option))
        {
            endpoint += $"?option={Uri.EscapeDataString(option)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Sets the framing assistant rotation angle
    /// </summary>
    /// <param name="rotation">Rotation angle in degrees</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set rotation operation status</returns>
    public async Task<Result<string>> SetFramingAssistantRotationAsync(double rotation, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting framing assistant rotation: {Rotation}°", rotation);
        var endpoint = $"/v2/api/framing/set-rotation?rotation={rotation}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Determines the rotation angle from the camera
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the determine rotation operation status</returns>
    public async Task<Result<string>> DetermineFramingAssistantRotationAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Determining framing assistant rotation from camera");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/framing/determine-rotation", cancellationToken));
    }

    #endregion

    #region Guider Equipment Methods

    /// <summary>
    /// Gets guider equipment information
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing guider equipment info</returns>
    public async Task<Result<GuiderInfoResponse>> GetGuiderInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting guider equipment information");
        return await ExecuteWithResilienceAsync(() => GetAsync<GuiderInfoResponse>("/v2/api/equipment/guider/info", cancellationToken));
    }

    /// <summary>
    /// Lists available guider devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing available guider devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetGuiderDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available guider devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/guider/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for available guider devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing updated list of available guider devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanGuiderDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for guider devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/guider/rescan", cancellationToken));
    }

    /// <summary>
    /// Connects to a guider device
    /// </summary>
    /// <param name="to">Device identifier to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing connection status</returns>
    public async Task<Result<string>> ConnectGuiderAsync(string? to = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to guider device - DeviceId: {DeviceId}", to ?? "default");
        
        var endpoint = "/v2/api/equipment/guider/connect";
        if (!string.IsNullOrEmpty(to))
        {
            endpoint += $"?to={Uri.EscapeDataString(to)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnects from the guider device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing disconnection status</returns>
    public async Task<Result<string>> DisconnectGuiderAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting from guider device");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/guider/disconnect", cancellationToken));
    }

    /// <summary>
    /// Starts guiding with optional calibration
    /// </summary>
    /// <param name="calibrate">Whether to calibrate before starting guiding (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing start guiding status</returns>
    public async Task<Result<string>> StartGuidingAsync(bool? calibrate = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting guiding - Calibrate: {Calibrate}", calibrate?.ToString() ?? "default");
        
        var endpoint = "/v2/api/equipment/guider/start";
        if (calibrate.HasValue)
        {
            endpoint += $"?calibrate={calibrate.Value.ToString().ToLowerInvariant()}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Stops guiding
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing stop guiding status</returns>
    public async Task<Result<string>> StopGuidingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping guiding");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/guider/stop", cancellationToken));
    }

    /// <summary>
    /// Clears guider calibration
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing clear calibration status</returns>
    public async Task<Result<string>> ClearGuiderCalibrationAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Clearing guider calibration");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/guider/clear-calibration", cancellationToken));
    }

    /// <summary>
    /// Gets guiding graph history (guide steps)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing guide steps history with RMS statistics</returns>
    public async Task<Result<GuideStepsHistory>> GetGuiderGraphAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting guider graph history");
        return await ExecuteWithResilienceAsync(() => GetAsync<GuideStepsHistory>("/v2/api/equipment/guider/graph", cancellationToken));
    }

    #endregion

    #region Image Methods

    /// <summary>
    /// Gets an image by index from the image history
    /// </summary>
    /// <param name="index">The index of the image to get</param>
    /// <param name="resize">Whether to resize the image</param>
    /// <param name="quality">The quality of the image, ranging from 1 (worst) to 100 (best). -1 or omitted for png</param>
    /// <param name="size">The size of the image ([width]x[height]). Requires resize to be true</param>
    /// <param name="stream">Stream the image to the client. This will stream the image in image/jpg or image/png format</param>
    /// <param name="debayer">Indicates if the image should be debayered</param>
    /// <param name="bayerPattern">What bayer pattern to use for debayering, if debayer is true</param>
    /// <param name="autoPrepare">Setting this to true will leave all processing up to NINA</param>
    /// <param name="imageType">Filter the image history by image type</param>
    /// <param name="rawFits">Whether to send the image (without streaming) as a raw FITS format</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing image data or stream</returns>
    public async Task<Result<ImageResponse>> GetImageAsync(
        int index,
        bool? resize = null,
        int? quality = null,
        string? size = null,
        bool? stream = null,
        bool? debayer = null,
        BayerPattern? bayerPattern = null,
        bool? autoPrepare = null,
        ImageType? imageType = null,
        bool? rawFits = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting image at index: {Index}", index);

        var queryParams = new List<string>();

        if (resize.HasValue)
            queryParams.Add($"resize={resize.Value.ToString().ToLower()}");

        if (quality.HasValue)
            queryParams.Add($"quality={quality.Value}");

        if (!string.IsNullOrEmpty(size))
            queryParams.Add($"size={Uri.EscapeDataString(size)}");

        if (stream.HasValue)
            queryParams.Add($"stream={stream.Value.ToString().ToLower()}");

        if (debayer.HasValue)
            queryParams.Add($"debayer={debayer.Value.ToString().ToLower()}");

        if (bayerPattern.HasValue)
            queryParams.Add($"bayerPattern={bayerPattern.Value}");

        if (autoPrepare.HasValue)
            queryParams.Add($"autoPrepare={autoPrepare.Value.ToString().ToLower()}");

        if (imageType.HasValue)
            queryParams.Add($"imageType={imageType.Value}");

        if (rawFits.HasValue)
            queryParams.Add($"raw_fits={rawFits.Value.ToString().ToLower()}");

        var endpoint = $"/v2/api/image/{index}";
        if (queryParams.Count > 0)
        {
            endpoint += "?" + string.Join("&", queryParams);
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<ImageResponse>(endpoint, cancellationToken));
    }

    /// <summary>
/// Gets image history. Only one parameter is required
/// </summary>
/// <param name="all">Whether to get all images or only the current image</param>
/// <param name="index">The index of the image to get</param>
/// <param name="count">Whether to count the number of images</param>
/// <param name="imageType">Filter by image type. This will restrict the result to images of the specified type</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>The result containing image history data or count</returns>
    public async Task<Result<ImageHistoryResponse>> GetImageHistoryAsync(
        bool? all = null,
        int? index = null,
        bool? count = null,
        ImageType? imageType = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting image history - All: {All}, Index: {Index}, Count: {Count}, ImageType: {ImageType}", 
            all, index, count, imageType);

        var queryParams = new List<string>();

        if (all.HasValue)
            queryParams.Add($"all={all.Value.ToString().ToLower()}");

        if (index.HasValue)
            queryParams.Add($"index={index.Value}");

        if (count.HasValue)
            queryParams.Add($"count={count.Value.ToString().ToLower()}");

        if (imageType.HasValue)
            queryParams.Add($"imageType={imageType.Value}");

        var endpoint = "/v2/api/image-history";
        if (queryParams.Count > 0)
        {
            endpoint += "?" + string.Join("&", queryParams);
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<ImageHistoryResponse>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Gets the thumbnail of an image. This requires Create Thumbnails to be enabled in NINA.
    /// This thumbnail has a width of 256px.
    /// </summary>
    /// <param name="index">The index of the image to get</param>
    /// <param name="imageType">Filter the image history by image type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing thumbnail data</returns>
    public async Task<Result<byte[]>> GetImageThumbnailAsync(
        int index,
        ImageType? imageType = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting image thumbnail at index: {Index}, ImageType: {ImageType}", index, imageType);

        var queryParams = new List<string>();

        if (imageType.HasValue)
            queryParams.Add($"imageType={imageType.Value}");

        var endpoint = $"/v2/api/image/thumbnail/{index}";
        if (queryParams.Count > 0)
        {
            endpoint += "?" + string.Join("&", queryParams);
        }

        // Use the resilience wrapper for consistent retry and circuit breaker behavior
        return await ExecuteWithResilienceAsync(() => GetThumbnailDataAsync(endpoint, cancellationToken));
    }

    /// <summary>
    /// Helper method to handle binary thumbnail data retrieval with consistent error handling
    /// </summary>
    private async Task<Result<byte[]>> GetThumbnailDataAsync(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogTrace("GET request to {Endpoint} for binary thumbnail data", endpoint);

            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("GET request failed - Endpoint: {Endpoint}, Status: {StatusCode}, Content: {Content}", 
                    endpoint, response.StatusCode, content);
                
                // Use the same exception mapping as other methods for consistency
                var httpException = NinaApiExceptionMapper.MapHttpStatusToException(
                    response.StatusCode, content, endpoint);
                return Result<byte[]>.Failure(httpException);
            }

            // For binary data (thumbnails), read directly as byte array
            var thumbnailData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            
            // Validate that we actually received data
            if (thumbnailData == null || thumbnailData.Length == 0)
            {
                _logger.LogWarning("API returned empty thumbnail data for {Endpoint}", endpoint);
                var emptyDataException = new NinaApiLogicalException("API returned empty thumbnail data", endpoint);
                return Result<byte[]>.Failure(emptyDataException);
            }
            
            _logger.LogTrace("Successfully retrieved {ByteCount} bytes of thumbnail data from {Endpoint}", 
                thumbnailData.Length, endpoint);
            return Result<byte[]>.Success(thumbnailData);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timeout for {Endpoint}", endpoint);
            // Use NINA-specific connection exception for timeouts
            var timeoutException = new NinaConnectionException($"Request to {endpoint} timed out", ex);
            return Result<byte[]>.Failure(timeoutException);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Request cancelled for {Endpoint}", endpoint);
            // User cancellation - don't treat as error, just propagate
            return Result<byte[]>.Failure(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request exception for {Endpoint}", endpoint);
            // Use NINA-specific connection exception for HTTP issues
            var connectionException = new NinaConnectionException($"HTTP request failed for {endpoint}: {ex.Message}", ex);
            return Result<byte[]>.Failure(connectionException);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting thumbnail data from {Endpoint}", endpoint);
            // Use base NINA exception for unexpected errors
            var unexpectedException = new NinaApiException($"Unexpected error occurred while getting thumbnail from {endpoint}: {ex.Message}", ex);
            return Result<byte[]>.Failure(unexpectedException);
        }
    }

    #endregion

    #region Mount Equipment Methods

    /// <summary>
    /// Gets information about the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing mount information</returns>
    public async Task<Result<MountInfo>> GetMountInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting mount information");
        return await ExecuteWithResilienceAsync(() => GetAsync<MountInfo>("/v2/api/equipment/mount/info", cancellationToken));
    }

    /// <summary>
    /// Gets list of available mount devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available mount devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetMountDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available mount devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/mount/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new mount devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available mount devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanMountDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for mount devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/mount/rescan", cancellationToken));
    }

    /// <summary>
    /// Connects to a mount device
    /// </summary>
    /// <param name="deviceId">The ID of the mount device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectMountAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to mount device: {DeviceId}", deviceId ?? "default");
        
        var endpoint = "/v2/api/equipment/mount/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }
        
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnects the currently connected mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectMountAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting mount");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/mount/disconnect", cancellationToken));
    }

    /// <summary>
    /// Homes the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the home operation status</returns>
    public async Task<Result<string>> HomeMountAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Homing mount");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/mount/home", cancellationToken));
    }

    /// <summary>
    /// Sets the mount tracking mode
    /// 0: Sidereal, 1: Lunar, 2: Solar, 3: King, 4: Stopped
    /// </summary>
    /// <param name="mode">The tracking mode to set (0-4)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the tracking mode operation status</returns>
    public async Task<Result<string>> SetMountTrackingModeAsync(int mode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting mount tracking mode to: {Mode}", mode);
        var endpoint = $"/v2/api/equipment/mount/tracking?mode={mode}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Parks the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the park operation status</returns>
    public async Task<Result<string>> ParkMountAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parking mount");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/mount/park", cancellationToken));
    }

    /// <summary>
    /// Unparks the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the unpark operation status</returns>
    public async Task<Result<string>> UnparkMountAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Unparking mount");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/mount/unpark", cancellationToken));
    }

    /// <summary>
    /// Performs a meridian flip to the current coordinates. This will only flip the mount if it is needed,
    /// it will not force the mount to flip
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the flip operation status</returns>
    public async Task<Result<string>> FlipMountAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing meridian flip");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/mount/flip", cancellationToken));
    }

    /// <summary>
    /// Performs a slew to the provided coordinates
    /// </summary>
    /// <param name="ra">The RA angle of the target in degrees</param>
    /// <param name="dec">The Dec angle of the target in degrees</param>
    /// <param name="waitForResult">Whether to wait for the slew to finish</param>
    /// <param name="center">Whether to center the telescope on the target</param>
    /// <param name="rotate">Whether to perform a center and rotate</param>
    /// <param name="rotationAngle">The rotation angle in degrees</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the slew operation status</returns>
    public async Task<Result<string>> SlewMountAsync(
        double ra,
        double dec,
        bool? waitForResult = null,
        bool? center = null,
        bool? rotate = null,
        double? rotationAngle = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Slewing mount to RA: {RA}°, Dec: {Dec}°", ra, dec);

        var queryParams = new List<string>
        {
            $"ra={ra}",
            $"dec={dec}"
        };

        if (waitForResult.HasValue)
            queryParams.Add($"waitForResult={waitForResult.Value.ToString().ToLower()}");

        if (center.HasValue)
            queryParams.Add($"center={center.Value.ToString().ToLower()}");

        if (rotate.HasValue)
            queryParams.Add($"rotate={rotate.Value.ToString().ToLower()}");

        if (rotationAngle.HasValue)
            queryParams.Add($"rotationAngle={rotationAngle.Value}");

        var endpoint = "/v2/api/equipment/mount/slew?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Stops any slew, even if it was not started using the API. However this is mainly useful for slews you issued
    /// yourself, since it doesn't completely abort slew&centers started by NINA. Therefore the recommended use is
    /// to stop simple slews without center or rotate. With center or rotate, this may take a few seconds to complete.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop slew operation status</returns>
    public async Task<Result<string>> StopMountSlewAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping mount slew");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/mount/slew/stop", cancellationToken));
    }

    /// <summary>
    /// Sets the current mount position as park position. This requires the mount to be unparked.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set park position operation status</returns>
    public async Task<Result<string>> SetMountParkPositionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting mount park position");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/mount/set-park-position", cancellationToken));
    }

    /// <summary>
    /// Sync the scope, either by manually supplying the coordinates or by solving and syncing.
    /// If the coordinates are omitted, a platesolve will be performed.
    /// </summary>
    /// <param name="ra">Right ascension in degrees (optional)</param>
    /// <param name="dec">Declination in degrees (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the sync operation status</returns>
    public async Task<Result<string>> SyncMountAsync(double? ra = null, double? dec = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Syncing mount - RA: {RA}, Dec: {Dec}", ra?.ToString() ?? "auto", dec?.ToString() ?? "auto");

        var endpoint = "/v2/api/equipment/mount/sync";
        var queryParams = new List<string>();

        if (ra.HasValue)
            queryParams.Add($"ra={ra.Value}");

        if (dec.HasValue)
            queryParams.Add($"dec={dec.Value}");

        if (queryParams.Count > 0)
        {
            endpoint += "?" + string.Join("&", queryParams);
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    #endregion

    #region Rotator Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected rotator
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the rotator information</returns>
    public async Task<Result<RotatorInfoResponse>> GetRotatorInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting rotator information");
        return await ExecuteWithResilienceAsync(() => GetAsync<RotatorInfoResponse>("/v2/api/equipment/rotator/info", cancellationToken));
    }

    /// <summary>
    /// Connect to the selected rotator device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status</returns>
    public async Task<Result<string>> ConnectRotatorAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to rotator");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/rotator/connect", cancellationToken));
    }

    /// <summary>
    /// Disconnect from the currently connected rotator device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status</returns>
    public async Task<Result<string>> DisconnectRotatorAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting rotator");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/rotator/disconnect", cancellationToken));
    }

    /// <summary>
    /// Get a list of all available rotator devices that can be connected to
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available rotator devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetRotatorDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available rotator devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/rotator/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescan for available rotator devices and update the device list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available rotator devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanRotatorDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for rotator devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/rotator/rescan", cancellationToken));
    }

    /// <summary>
    /// Move the rotator to the specified position in degrees
    /// </summary>
    /// <param name="position">Target position in degrees (0-360)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the move operation status</returns>
    public async Task<Result<string>> MoveRotatorAsync(double position, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Moving rotator to position: {Position} degrees", position);
        var endpoint = $"/v2/api/equipment/rotator/move?position={position}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Move the rotator to the specified mechanical position in degrees
    /// </summary>
    /// <param name="position">Target mechanical position in degrees</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the mechanical move operation status</returns>
    public async Task<Result<string>> MoveRotatorMechanicalAsync(double position, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Moving rotator to mechanical position: {Position} degrees", position);
        var endpoint = $"/v2/api/equipment/rotator/move-mechanical?position={position}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    #endregion

    #region Safety Monitor Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected safety monitor
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the safety monitor information</returns>
    public async Task<Result<SafetyMonitorInfoResponse>> GetSafetyMonitorInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting safety monitor information");
        return await ExecuteWithResilienceAsync(() => GetAsync<SafetyMonitorInfoResponse>("/v2/api/equipment/safetymonitor/info", cancellationToken));
    }

    /// <summary>
    /// Connect to a safety monitor device
    /// </summary>
    /// <param name="deviceId">The ID of the safety monitor device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectSafetyMonitorAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to safety monitor device - DeviceId: {DeviceId}", deviceId ?? "default");

        var endpoint = "/v2/api/equipment/safetymonitor/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?deviceId={Uri.EscapeDataString(deviceId)}";
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnect from the currently connected safety monitor
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectSafetyMonitorAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting safety monitor");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/safetymonitor/disconnect", cancellationToken));
    }

    /// <summary>
    /// Gets list of available safety monitor devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available safety monitor devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetSafetyMonitorDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available safety monitor devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/safetymonitor/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new safety monitor devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available safety monitor devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanSafetyMonitorDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for safety monitor devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/safetymonitor/rescan", cancellationToken));
    }

    #endregion

    #region Switch Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected switch
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the switch information</returns>
    public async Task<Result<SwitchInfoResponse>> GetSwitchInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting switch information");
        return await ExecuteWithResilienceAsync(() => GetAsync<SwitchInfoResponse>("/v2/api/equipment/switch/info", cancellationToken));
    }

    /// <summary>
    /// Connect to a switch device
    /// </summary>
    /// <param name="deviceId">The ID of the switch device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectSwitchAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to switch device - DeviceId: {DeviceId}", deviceId ?? "default");

        var endpoint = "/v2/api/equipment/switch/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnect from the currently connected switch
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectSwitchAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting switch");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/switch/disconnect", cancellationToken));
    }

    /// <summary>
    /// Gets list of available switch devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available switch devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetSwitchDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available switch devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/switch/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new switch devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available switch devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanSwitchDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for switch devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/switch/rescan", cancellationToken));
    }

    /// <summary>
    /// Set switch value at the specified index
    /// </summary>
    /// <param name="index">The index of the switch to set</param>
    /// <param name="value">The value to set</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set value operation status</returns>
    public async Task<Result<string>> SetSwitchValueAsync(int index, double value, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting switch value - Index: {Index}, Value: {Value}", index, value);
        
        var queryParams = new List<string>
        {
            $"index={index}",
            $"value={value}"
        };

        var endpoint = $"/v2/api/equipment/switch/set?{string.Join("&", queryParams)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    #endregion

    #region Weather Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected weather device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the weather information</returns>
    public async Task<Result<WeatherInfoResponse>> GetWeatherInfoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting weather information");
        return await ExecuteWithResilienceAsync(() => GetAsync<WeatherInfoResponse>("/v2/api/equipment/weather/info", cancellationToken));
    }

    /// <summary>
    /// Connect to a weather device
    /// </summary>
    /// <param name="deviceId">The ID of the weather device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    public async Task<Result<string>> ConnectWeatherAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to weather device - DeviceId: {DeviceId}", deviceId ?? "default");

        var endpoint = "/v2/api/equipment/weather/connect";
        if (!string.IsNullOrEmpty(deviceId))
        {
            endpoint += $"?to={Uri.EscapeDataString(deviceId)}";
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Disconnect from the currently connected weather device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    public async Task<Result<string>> DisconnectWeatherAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting weather device");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/equipment/weather/disconnect", cancellationToken));
    }

    /// <summary>
    /// Gets list of available weather devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available weather devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> GetWeatherDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available weather devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/weather/list-devices", cancellationToken));
    }

    /// <summary>
    /// Rescans for new weather devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available weather devices</returns>
    public async Task<Result<IReadOnlyList<DeviceInfo>>> RescanWeatherDevicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Rescanning for weather devices");
        // Now GetAsync<IReadOnlyList<T>> automatically handles the conversion from List<T>
        return await ExecuteWithResilienceAsync(() => GetAsync<IReadOnlyList<DeviceInfo>>("/v2/api/equipment/weather/rescan", cancellationToken));
    }

    #endregion

    #region Livestack Methods

    /// <summary>
    /// Starts Livestack, requires Livestack >= v1.0.0.9. Note that this method cannot fail, 
    /// even if the livestack plugin is not installed or something went wrong. 
    /// This simply issues a command to start the livestack.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the start livestack operation status</returns>
    public async Task<Result<string>> StartLivestackAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting livestack");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/livestack/start", cancellationToken));
    }

    /// <summary>
    /// Stops Livestack, requires Livestack >= v1.0.0.9. Note that this method cannot fail, 
    /// even if the livestack plugin is not installed or something went wrong. 
    /// This simply issues a command to stop the livestack.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop livestack operation status</returns>
    public async Task<Result<string>> StopLivestackAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping livestack");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/livestack/stop", cancellationToken));
    }

    /// <summary>
    /// Gets the stacked image from the livestack plugin for a given target and filter.
    /// </summary>
    /// <param name="target">The target name (e.g., "M31")</param>
    /// <param name="filter">The filter name (e.g., "RGB")</param>
    /// <param name="resize">Whether to resize the image</param>
    /// <param name="quality">The quality of the image, ranging from 1 (worst) to 100 (best). -1 or omitted for png</param>
    /// <param name="size">The size of the image ([width]x[height]). Requires resize to be true</param>
    /// <param name="scale">The scale of the image. Requires resize to be true</param>
    /// <param name="stream">Stream the image to the client</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stacked image data</returns>
    public async Task<Result<string>> GetLivestackImageAsync(
        string target,
        string filter,
        bool? resize = null,
        int? quality = null,
        string? size = null,
        double? scale = null,
        bool? stream = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting livestack image - Target: {Target}, Filter: {Filter}", target, filter);

        var queryParams = new List<string>();

        if (resize.HasValue)
            queryParams.Add($"resize={resize.Value.ToString().ToLower()}");

        if (quality.HasValue)
            queryParams.Add($"quality={quality.Value}");

        if (!string.IsNullOrEmpty(size))
            queryParams.Add($"size={Uri.EscapeDataString(size)}");

        if (scale.HasValue)
            queryParams.Add($"scale={scale.Value}");

        if (stream.HasValue)
            queryParams.Add($"stream={stream.Value.ToString().ToLower()}");

        var endpoint = $"/v2/api/livestack/{Uri.EscapeDataString(target)}/{Uri.EscapeDataString(filter)}";
        if (queryParams.Count > 0)
        {
            endpoint += "?" + string.Join("&", queryParams);
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    #endregion

    #region Sequence Methods

    /// <summary>
    /// Get sequence as JSON. For this to work, there needs to be a deep sky object container 
    /// present and the sequencer view has to be initialized. This endpoint is generally 
    /// advised to use over state since it gives more reliable results.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the sequence as JSON</returns>
    public async Task<Result<SequenceOrGlobalTriggers>> GetSequenceJsonAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting sequence as JSON");
        return await ExecuteWithResilienceAsync(() => GetAsync<SequenceOrGlobalTriggers>("/v2/api/sequence/json", cancellationToken));
    }

    /// <summary>
    /// Get complete sequence as JSON. For this to work, there needs to be a deep sky object 
    /// container present and the sequencer view has to be initialized. This is similar to 
    /// the json endpoint, however the returned sequence is much more elaborate and also 
    /// supports plugins. Use this endpoint (not json!) as reference for sequence editing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the complete sequence state</returns>
    public async Task<Result<SequenceOrGlobalTriggers>> GetSequenceStateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting complete sequence state");
        return await ExecuteWithResilienceAsync(() => GetAsync<SequenceOrGlobalTriggers>("/v2/api/sequence/state", cancellationToken));
    }

    /// <summary>
    /// Edit a sequence. This works similarly to profile/change-value. Note that this mainly 
    /// supports fields that expect simple types like strings, numbers etc, and may not work 
    /// for things like enums or objects (filter, time source, ...). Use 'sequence/state' 
    /// as reference, not 'sequence/json'.
    /// </summary>
    /// <param name="path">The path to the property that should be updated. Use `GlobalTriggers`, `Start`, `Imaging`, `End` for the sequence root containers. Then use the name of the property or the index of the item in a list, separated with `-`.</param>
    /// <param name="value">The new value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the operation status</returns>
    public async Task<Result<string>> EditSequenceAsync(string path, string value, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Editing sequence - Path: {Path}, Value: {Value}", path, value);

        var queryParams = new List<string>
        {
            $"path={Uri.EscapeDataString(path)}",
            $"value={Uri.EscapeDataString(value)}"
        };

        var endpoint = $"/v2/api/sequence/edit?{string.Join("&", queryParams)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Start sequence. This requires the sequencer to be initialized, which can be achieved 
    /// by opening the tab once.
    /// </summary>
    /// <param name="skipValidation">Skip validation of the sequence</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the start operation status</returns>
    public async Task<Result<string>> StartSequenceAsync(bool? skipValidation = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sequence - SkipValidation: {SkipValidation}", skipValidation);

        var endpoint = "/v2/api/sequence/start";

        if (skipValidation.HasValue)
        {
            endpoint += $"?skipValidation={skipValidation.Value.ToString().ToLower()}";
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Stop sequence. This requires the sequencer to be initialized, which can be achieved 
    /// by opening the tab once.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop operation status</returns>
    public async Task<Result<string>> StopSequenceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping sequence");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/sequence/stop", cancellationToken));
    }

    /// <summary>
    /// Reset sequence. This requires the sequencer to be initialized, which can be achieved 
    /// by opening the tab once.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the reset operation status</returns>
    public async Task<Result<string>> ResetSequenceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resetting sequence");
        return await ExecuteWithResilienceAsync(() => GetAsync<string>("/v2/api/sequence/reset", cancellationToken));
    }

    /// <summary>
    /// List available sequences. This is currently not really useful as it is not possible 
    /// to load sequences.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available sequences</returns>
    public async Task<Result<AvailableSequencesResponse>> ListAvailableSequencesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing available sequences");
        return await ExecuteWithResilienceAsync(() => GetAsync<AvailableSequencesResponse>("/v2/api/sequence/list-available", cancellationToken));
    }

    /// <summary>
    /// Set the target of any one of the active target containers in the sequence.
    /// </summary>
    /// <param name="name">The target name</param>
    /// <param name="ra">The RA coordinate in degrees</param>
    /// <param name="dec">The DEC coordinate in degrees</param>
    /// <param name="rotation">The target rotation</param>
    /// <param name="index">The index of the target container to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set target operation status</returns>
    public async Task<Result<string>> SetSequenceTargetAsync(
        string name, 
        double ra, 
        double dec, 
        double rotation, 
        int index, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting sequence target - Name: {Name}, RA: {RA}, DEC: {DEC}, Rotation: {Rotation}, Index: {Index}", 
            name, ra, dec, rotation, index);

        var queryParams = new List<string>
        {
            $"name={Uri.EscapeDataString(name)}",
            $"ra={ra}",
            $"dec={dec}",
            $"rotation={rotation}",
            $"index={index}"
        };

        var endpoint = $"/v2/api/sequence/set-target?{string.Join("&", queryParams)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Load a sequence from a file from the default sequence folders. The names can be 
    /// retrieved using the ListAvailableSequences endpoint.
    /// </summary>
    /// <param name="sequenceName">The name of the sequence to load</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the load operation status</returns>
    public async Task<Result<string>> LoadSequenceFromFileAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading sequence from file - SequenceName: {SequenceName}", sequenceName);

        var endpoint = $"/v2/api/sequence/load?sequenceName={Uri.EscapeDataString(sequenceName)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Load a sequence from JSON supplied by the client.
    /// </summary>
    /// <param name="sequenceJson">The sequence JSON data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the load operation status</returns>
    public async Task<Result<string>> LoadSequenceFromJsonAsync(string sequenceJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading sequence from JSON data");

        return await ExecuteWithResilienceAsync(async () =>
        {
            try
            {
                var httpContent = new StringContent(sequenceJson, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/v2/api/sequence/load", httpContent, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("LoadSequenceFromJsonAsync failed with status code: {StatusCode}, Response: {Response}", 
                        response.StatusCode, responseContent);
                    
                    // Use the same exception mapping as other methods for consistency
                    var httpException = NinaApiExceptionMapper.MapHttpStatusToException(
                        response.StatusCode, responseContent, "/v2/api/sequence/load");
                    return Result<string>.Failure(httpException);
                }

                try
                {
                    var ninaResponse = JsonSerializer.Deserialize<NinaApiResponse<string>>(responseContent, _jsonOptions);
                    if (ninaResponse?.Success == true && ninaResponse.Response != null)
                    {
                        return Result<string>.Success(ninaResponse.Response);
                    }
                    else
                    {
                        var errorMessage = string.IsNullOrEmpty(ninaResponse?.Error) ? "Unknown error from API" : ninaResponse.Error;
                        _logger.LogWarning("API returned error response: {Error}", errorMessage);
                        
                        var logicalException = NinaApiExceptionMapper.MapApiErrorToException(errorMessage, "/v2/api/sequence/load");
                        return Result<string>.Failure(logicalException);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize response from LoadSequenceFromJsonAsync");
                    var jsonException = new NinaApiException($"Failed to parse response from /v2/api/sequence/load: {ex.Message}", ex);
                    return Result<string>.Failure(jsonException);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed for LoadSequenceFromJsonAsync");
                var connectionException = new NinaConnectionException($"HTTP request failed for /v2/api/sequence/load: {ex.Message}", ex);
                return Result<string>.Failure(connectionException);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, "Request timeout for LoadSequenceFromJsonAsync");
                var timeoutException = new NinaConnectionException($"Request to /v2/api/sequence/load timed out", ex);
                return Result<string>.Failure(timeoutException);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogDebug("LoadSequenceFromJsonAsync was cancelled");
                return Result<string>.Failure(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in LoadSequenceFromJsonAsync");
                var unexpectedException = new NinaApiException($"Unexpected error occurred while loading sequence: {ex.Message}", ex);
                return Result<string>.Failure(unexpectedException);
            }
        });
    }

    #endregion

    #region Profile Methods

    /// <summary>
    /// Shows the profile information
    /// </summary>
    /// <param name="active">Whether to show the active profile or a list of all available profiles</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing profile information or list of profiles</returns>
    public async Task<Result<ProfileResponse>> ShowProfileAsync(bool? active = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting profile information - Active: {Active}", active?.ToString() ?? "all");

        var endpoint = "/v2/api/profile/show";
        if (active.HasValue)
        {
            endpoint += $"?active={active.Value.ToString().ToLower()}";
        }

        return await ExecuteWithResilienceAsync(() => GetAsync<ProfileResponse>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Changes a value in the profile
    /// </summary>
    /// <param name="settingPath">The path to the setting to change (e.g., "CameraSettings-PixelSize"). This refers to the profile structure like it is received when using ShowProfileAsync. Separate each object with a dash (-)</param>
    /// <param name="newValue">The new value to set</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the operation status</returns>
    public async Task<Result<string>> ChangeProfileValueAsync(string settingPath, object newValue, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Changing profile value - Path: {SettingPath}, NewValue: {NewValue}", settingPath, newValue);

        var queryParams = new List<string>
        {
            $"settingpath={Uri.EscapeDataString(settingPath)}",
            $"newValue={Uri.EscapeDataString(newValue?.ToString() ?? string.Empty)}"
        };

        var endpoint = "/v2/api/profile/change-value?" + string.Join("&", queryParams);
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Switches the profile
    /// </summary>
    /// <param name="profileId">The ID of the profile to switch to. This ID can be retrieved using ShowProfileAsync with active=false</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the switch operation status</returns>
    public async Task<Result<string>> SwitchProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Switching profile to: {ProfileId}", profileId);

        var endpoint = $"/v2/api/profile/switch?profileid={Uri.EscapeDataString(profileId)}";
        return await ExecuteWithResilienceAsync(() => GetAsync<string>(endpoint, cancellationToken));
    }

    /// <summary>
    /// Gets the horizon for the active profile
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the horizon data with altitudes and azimuths</returns>
    public async Task<Result<HorizonData>> GetProfileHorizonAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting profile horizon data");
        return await ExecuteWithResilienceAsync(() => GetAsync<HorizonData>("/v2/api/profile/horizon", cancellationToken));
    }

    #endregion


    #region Helper Methods with Enhanced Resilience

    /// <summary>
    /// Execute operation with resilience patterns (retry, circuit breaker)
    /// 
    /// This method implements a two-layer resilience pattern:
    /// 1. Retry Policy: Handles transient failures with exponential backoff
    /// 2. Circuit Breaker: Prevents cascading failures by "opening" after threshold failures
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="operation">Operation to execute</param>
    /// <returns>Result of the operation</returns>
    private async Task<Result<T>> ExecuteWithResilienceAsync<T>(Func<Task<Result<T>>> operation)
    {
        // Ensure client hasn't been disposed
        if (_disposed)
            throw new ObjectDisposedException(nameof(NinaApiClient));

        // LAYER 1: Retry Policy - Handles transient failures
        // This applies exponential backoff retry logic for network timeouts, 
        // temporary API errors, and other recoverable failures
        var retryResult = await RetryPolicy.ExecuteWithRetryAsync(
            operation,                                              // The actual API call to execute
            _options.MaxRetryAttempts,                             // Max attempts (e.g., 3)
            TimeSpan.FromMilliseconds(_options.RetryDelayMs),      // Base delay between retries (e.g., 1000ms)
            _logger);                                              // Logger for retry attempts

        // LAYER 2: Circuit Breaker - Prevents cascading failures
        // If enabled, this wraps the retry result to track failure patterns
        // and "open" the circuit if too many failures occur
        if (_circuitBreaker != null)
        {
            // Circuit breaker evaluates the retry result and decides:
            // - CLOSED: Normal operation, passes through the result
            // - OPEN: Too many failures, immediately fails without calling operation
            // - HALF-OPEN: Testing recovery, allows one attempt to check if service is healthy
            return await _circuitBreaker.ExecuteAsync(() => Task.FromResult(retryResult));
        }

        // If no circuit breaker configured, return the retry result directly
        return retryResult;
    }

    private async Task<Result<T>> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            _logger.LogTrace("GET request to {Endpoint}", endpoint);

            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GET request failed - Endpoint: {Endpoint}, Status: {StatusCode}, Content: {Content}", 
                    endpoint, response.StatusCode, content);
                
                var httpException = NinaApiExceptionMapper.MapHttpStatusToException(
                    response.StatusCode, content, endpoint);
                return Result<T>.Failure(httpException);
            }

            // Determine the actual type to deserialize based on the requested type T
            var deserializationType = GetDeserializationType<T>();
            
            // Deserialize to the appropriate type
            var jsonDocument = JsonDocument.Parse(content);
            var responseElement = jsonDocument.RootElement.GetProperty("Response");
            var successElement = jsonDocument.RootElement.TryGetProperty("Success", out var success) ? success : default;
            var errorElement = jsonDocument.RootElement.TryGetProperty("Error", out var error) ? error : default;
            
            if (successElement.ValueKind != JsonValueKind.Undefined && !successElement.GetBoolean())
            {
                var apiError = errorElement.ValueKind != JsonValueKind.Undefined ? errorElement.GetString() : "Unknown API error";
                _logger.LogWarning("API returned error response: {Error}", apiError);
                
                var logicalException = NinaApiExceptionMapper.MapApiErrorToException(apiError ?? "Unknown API error", endpoint);
                return Result<T>.Failure(logicalException);
            }

            if (responseElement.ValueKind == JsonValueKind.Null || responseElement.ValueKind == JsonValueKind.Undefined)
            {
                _logger.LogWarning("API returned null data for {Endpoint}", endpoint);
                var nullDataException = new NinaApiLogicalException("API returned null data", endpoint);
                return Result<T>.Failure(nullDataException);
            }

            // Deserialize to the intermediate type
            var deserializedData = JsonSerializer.Deserialize(responseElement.GetRawText(), deserializationType, _jsonOptions);
            
            if (deserializedData == null)
            {
                _logger.LogWarning("Failed to deserialize response data for {Endpoint}", endpoint);
                var deserializationException = new NinaApiLogicalException("Failed to deserialize response data", endpoint);
                return Result<T>.Failure(deserializationException);
            }

            // Convert to the requested type T
            var convertedData = ConvertToRequestedType<T>(deserializedData);
            
            _logger.LogTrace("Successfully retrieved data from {Endpoint}", endpoint);
            return Result<T>.Success(convertedData);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timeout for {Endpoint}", endpoint);
            var timeoutException = new NinaConnectionException($"Request to {endpoint} timed out", ex);
            return Result<T>.Failure(timeoutException);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request exception for {Endpoint}", endpoint);
            var connectionException = new NinaConnectionException($"HTTP request failed for {endpoint}: {ex.Message}", ex);
            return Result<T>.Failure(connectionException);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization failed for {Endpoint}", endpoint);
            var jsonException = new NinaApiException($"Failed to parse response from {endpoint}: {ex.Message}", ex);
            return Result<T>.Failure(jsonException);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting data from {Endpoint}", endpoint);
            var unexpectedException = new NinaApiException($"Unexpected error occurred while calling {endpoint}: {ex.Message}", ex);
            return Result<T>.Failure(unexpectedException);
        }
    }

    /// <summary>
    /// Gets the actual type to deserialize based on the requested return type
    /// </summary>
    private static Type GetDeserializationType<T>()
    {
        var requestedType = typeof(T);
        
        // If T is IReadOnlyList<TElement>, we need to deserialize to List<TElement>
        if (requestedType.IsGenericType && requestedType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var elementType = requestedType.GetGenericArguments()[0];
            return typeof(List<>).MakeGenericType(elementType);
        }
        
        // For other types, deserialize to the requested type directly
        return requestedType;
    }

    /// <summary>
    /// Converts the deserialized data to the requested type T
    /// </summary>
    private static T ConvertToRequestedType<T>(object deserializedData) where T : class
    {
        var requestedType = typeof(T);
        
        // If T is IReadOnlyList<TElement> and data is List<TElement>, convert to ReadOnlyCollection
        if (requestedType.IsGenericType && requestedType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            if (deserializedData is System.Collections.IList list)
            {
                var elementType = requestedType.GetGenericArguments()[0];
                var readOnlyListType = typeof(System.Collections.ObjectModel.ReadOnlyCollection<>).MakeGenericType(elementType);
                var readOnlyList = Activator.CreateInstance(readOnlyListType, list);
                return (T)readOnlyList!;
            }
        }
        
        // For other types, return as-is (already the correct type)
        return (T)deserializedData;
    }

    /// <summary>
    /// Gets diagnostics information about the client
    /// </summary>
    /// <returns>Diagnostics information</returns>
    public NinaClientDiagnostics GetDiagnostics()
    {
        var bufferStats = _bufferManager.GetStatistics();
        
        return new NinaClientDiagnostics
        {
            IsDisposed = _disposed,
            BaseUrl = _options.BaseUrl,
            TimeoutSeconds = _options.TimeoutSeconds,
            CircuitBreakerEnabled = _options.EnableCircuitBreaker,
            CircuitBreakerState = _circuitBreaker?.State.ToString(),
            CircuitBreakerFailureCount = _circuitBreaker?.FailureCount ?? 0,
            BufferStatistics = bufferStats
        };
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _circuitBreaker?.Dispose();
            _bufferManager?.Dispose();
            _httpClient?.Dispose();
            
            _logger.LogDebug("NinaApiClient disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during NinaApiClient disposal");
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Diagnostics information for the NINA API client
/// </summary>
public record NinaClientDiagnostics
{
    public bool IsDisposed { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public bool CircuitBreakerEnabled { get; init; }
    public string? CircuitBreakerState { get; init; }
    public int CircuitBreakerFailureCount { get; init; }
    public BufferStatistics BufferStatistics { get; init; } = new();
}
