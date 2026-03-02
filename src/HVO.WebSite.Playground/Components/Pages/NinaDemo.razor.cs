using Microsoft.AspNetCore.Components;
using HVO.NinaClient;
using HVO.NinaClient.Models;
using System.Timers;

namespace HVO.WebSite.Playground.Components.Pages
{
    /// <summary>
    /// Code-behind for the NINA API demonstration page
    /// </summary>
    public partial class NinaDemo : ComponentBase, IDisposable
    {
        [Inject] private INinaApiClient NinaApiClient { get; set; } = default!;
        [Inject] private INinaWebSocketClient NinaWebSocketClient { get; set; } = default!;
        [Inject] private ILogger<NinaDemo> Logger { get; set; } = default!;

        private string? _versionInfo;
        private MountInfo? _mountInfo;
        private CameraInfo? _cameraInfo;
        private FilterWheelInfo? _filterWheelInfo;
        private bool _isLoading;
        private string? _errorMessage;
        private DateTime? _lastUpdated;
        private bool _disposed;
        private bool _isWebSocketConnected;
        private System.Timers.Timer? _updateTimer;
        private string? _webSocketStatus = "Disconnected";
        private bool _isScreenshotLoading;
        private byte[]? _screenshotData;
        private bool _ninaAvailable = true; // Track if NINA is available

        // Image capture properties
        private bool _isCapturing;
        private double _exposureTime = 5.0;
        private string? _selectedFilter;
        private int _selectedFilterPosition = -1; // New: Track filter position instead of name
        private ImageStatistics? _lastImageStatistics;
        private string? _capturedImageData; // Base64 image data from capture
        private bool _isCameraConnected;
        private bool _isFilterWheelConnected;
        private List<FilterInfo> _availableFilters = new();
        private List<int> _availableBinnings = new() { 1, 2, 3, 4 };
        private DateTime _captureStartTime; // Track when capture started for timeout handling

        // Device selection properties
        private List<DeviceInfo> _availableCameraDevices = new();

        // Advanced capture properties (currently unused but may be implemented later)
        // These are placeholder fields for future functionality

        /// <summary>
        /// Initialize the component when it's first rendered
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        protected override async Task OnInitializedAsync()
        {
            Logger.LogDebug("Initializing NINA demo component");
            
            try
            {
                // Setup WebSocket event handlers
                SetupWebSocketEventHandlers();
                
                // Try to connect to NINA - non-blocking
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ConnectWebSocketAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "WebSocket connection failed during initialization - will retry later");
                    }
                });
                
                // Load initial data - this will determine if NINA is available
                await LoadDataAsync();
                
                // Only setup timer if NINA seems to be available
                if (_ninaAvailable)
                {
                    // Setup periodic refresh timer (every 5 seconds as fallback to WebSocket)
                    _updateTimer = new System.Timers.Timer(5000);
                    _updateTimer.Elapsed += OnTimerElapsed;
                    _updateTimer.AutoReset = true;
                    _updateTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during NINA demo component initialization");
                _errorMessage = $"Initialization error: {ex.Message}";
                _ninaAvailable = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Setup WebSocket event handlers for real-time updates
        /// </summary>
        private void SetupWebSocketEventHandlers()
        {
            try
            {
                NinaWebSocketClient.EventReceived += OnNinaEventReceived;
                NinaWebSocketClient.ConnectionStateChanged += OnWebSocketConnectionStateChanged;
                NinaWebSocketClient.ErrorOccurred += OnWebSocketErrorOccurred;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to setup WebSocket event handlers");
            }
        }

        /// <summary>
        /// Connect to NINA WebSocket server
        /// </summary>
        private async Task ConnectWebSocketAsync()
        {
            try
            {
                _webSocketStatus = "Connecting...";
                await InvokeAsync(StateHasChanged);
                
                var result = await NinaWebSocketClient.ConnectAsync();
                if (result.IsSuccessful)
                {
                    Logger.LogInformation("Successfully connected to NINA WebSocket server");
                    _webSocketStatus = "Connected";
                    _ninaAvailable = true;
                }
                else
                {
                    Logger.LogWarning("Failed to connect to NINA WebSocket server: {Error}", result.Error?.Message ?? "Unknown error");
                    _webSocketStatus = "Connection Failed";
                    _ninaAvailable = false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error connecting to NINA WebSocket server");
                _webSocketStatus = "Connection Error";
                _ninaAvailable = false;
            }
            finally
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Handle NINA WebSocket events for real-time updates
        /// </summary>
        private async void OnNinaEventReceived(object? sender, NinaEventArgs e)
        {
            try
            {
                Logger.LogInformation("✅ Received NINA event: {EventType} with data: {EventDataType}", 
                    e.EventType, e.EventData?.GetType()?.Name ?? "null");
                
                // Update mount information on relevant events
                if (IsMountRelatedEvent(e.EventType))
                {
                    Logger.LogDebug("Mount-related event received, refreshing mount information");
                    await RefreshMountInfoAsync();
                }

                // Handle camera and imaging events
                if (IsCameraRelatedEvent(e.EventType))
                {
                    Logger.LogDebug("Camera-related event received, refreshing camera information");
                    await RefreshCameraInfoAsync();
                    
                    // Handle API capture finished event specifically
                    if (e.EventType == NinaEventType.ApiCaptureFinished)
                    {
                        Logger.LogInformation("🎯 API capture finished event received - processing completion");
                        await HandleCaptureFinishedAsync();
                    }
                    
                    // Handle image save events specifically
                    if (e.EventType == NinaEventType.ImageSave && e.EventData is ImageStatistics imageStats)
                    {
                        _lastImageStatistics = imageStats;
                        Logger.LogInformation("📷 Image captured with statistics - Stars: {Stars}, HFR: {HFR:F2}", 
                            imageStats.Stars, imageStats.HFR);
                        await InvokeAsync(StateHasChanged);
                    }
                }

                // Handle filter wheel events
                if (IsFilterWheelRelatedEvent(e.EventType))
                {
                    Logger.LogDebug("Filter wheel event received, refreshing filter wheel information");
                    await RefreshFilterWheelInfoAsync();
                }

                // Log all events for debugging
                Logger.LogDebug("Event details - Type: {EventType}, Raw: {RawResponse}", 
                    e.EventType, e.RawResponse?.Type ?? "null");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling NINA WebSocket event: {EventType}", e.EventType);
            }
        }

        /// <summary>
        /// Handle capture finished event from WebSocket
        /// </summary>
        private async Task HandleCaptureFinishedAsync()
        {
            try
            {
                // Get the latest capture statistics first
                var statisticsResult = await NinaApiClient.GetCameraStatisticsAsync();
                if (statisticsResult.IsSuccessful)
                {
                    _lastImageStatistics = statisticsResult.Value;
                    Logger.LogInformation("Image capture completed successfully via WebSocket - Stars: {Stars}, HFR: {HFR:F2}, Mean: {Mean:F2}", 
                        _lastImageStatistics.Stars, _lastImageStatistics.HFR, _lastImageStatistics.Mean);
                }
                else
                {
                    Logger.LogWarning("Failed to get camera statistics after capture finished: {Error}", statisticsResult.Error?.Message ?? "Unknown error");
                }

                // Try to get the captured image by calling capture again with waitForResult=true and duration=0
                // This should retrieve the result of the last capture without taking a new exposure
                if (string.IsNullOrEmpty(_capturedImageData))
                {
                    try
                    {
                        Logger.LogDebug("Attempting to retrieve captured image data from last exposure");
                        
                        var imageResult = await NinaApiClient.CaptureAsync(
                            resize: true,
                            scale: 0.50,
                            quality: 75,
                            duration: 0,           // No new exposure - just get the last result
                            getResult: 1,          // Get the image data
                            waitForResult: true,   // Wait for the result data (should be immediate)
                            omitImage: false       // Include image data
                        );
                        
                        if (imageResult.IsSuccessful && imageResult.Value.IsCaptureResponse)
                        {
                            var captureData = imageResult.Value.AsCaptureResponse;
                            if (!string.IsNullOrEmpty(captureData?.Image))
                            {
                                _capturedImageData = captureData.Image;
                                Logger.LogInformation("Retrieved captured image data - Size: {Size} bytes", 
                                    Convert.FromBase64String(_capturedImageData).Length);
                            }
                            else
                            {
                                Logger.LogWarning("Capture response contained no image data");
                            }
                        }
                        else if (imageResult.IsSuccessful && imageResult.Value.IsStringResponse)
                        {
                            Logger.LogInformation("Image retrieval response: {Response}", imageResult.Value.AsStringResponse);
                            // Sometimes NINA returns a string response even when requesting results
                            // This might mean the image data isn't available through this method
                        }
                        else
                        {
                            Logger.LogWarning("Failed to retrieve captured image data: {Error}", 
                                imageResult.Error?.Message ?? "Unknown error");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Could not retrieve captured image data - this is not critical, we still have statistics");
                        // This is not a critical error - we still have the statistics
                        // The image might not be available through the API, which is common in NINA
                    }
                }

                // Update the capture state
                _isCapturing = false;
                _captureStartTime = default; // Reset capture start time
                _lastUpdated = DateTime.Now;
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling capture finished event");
                _isCapturing = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Handle WebSocket connection state changes
        /// </summary>
        private async void OnWebSocketConnectionStateChanged(object? sender, bool isConnected)
        {
            try
            {
                _isWebSocketConnected = isConnected;
                _webSocketStatus = isConnected ? "Connected" : "Disconnected";
                _ninaAvailable = isConnected;
                
                Logger.LogInformation("🔌 NINA WebSocket connection state changed: {IsConnected} - Status: {Status}", 
                    isConnected, _webSocketStatus);
                
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling WebSocket connection state change");
            }
        }

        /// <summary>
        /// Handle WebSocket errors
        /// </summary>
        private void OnWebSocketErrorOccurred(object? sender, Exception error)
        {
            Logger.LogError(error, "❌ NINA WebSocket error occurred: {ErrorMessage}", error.Message);
            _webSocketStatus = "Error";
            _ninaAvailable = false;
        }

        /// <summary>
        /// Check if event is related to mount status/position changes
        /// </summary>
        private static bool IsMountRelatedEvent(NinaEventType eventType)
        {
            return eventType == NinaEventType.MountConnected ||
                   eventType == NinaEventType.MountDisconnected ||
                   eventType == NinaEventType.MountParked ||
                   eventType == NinaEventType.MountUnparked ||
                   eventType == NinaEventType.MountHomed ||
                   eventType == NinaEventType.MountBeforeFlip ||
                   eventType == NinaEventType.MountAfterFlip ||
                   eventType == NinaEventType.MountCenter;
        }

        /// <summary>
        /// Check if event is related to camera status/operation changes
        /// </summary>
        private static bool IsCameraRelatedEvent(NinaEventType eventType)
        {
            return eventType == NinaEventType.CameraConnected ||
                   eventType == NinaEventType.CameraDisconnected ||
                   eventType == NinaEventType.ImageSave ||
                   eventType == NinaEventType.ApiCaptureFinished ||
                   eventType == NinaEventType.AutofocusFinished;
        }

        /// <summary>
        /// Check if event is related to filter wheel status/operation changes
        /// </summary>
        private static bool IsFilterWheelRelatedEvent(NinaEventType eventType)
        {
            return eventType == NinaEventType.FilterWheelConnected ||
                   eventType == NinaEventType.FilterWheelDisconnected ||
                   eventType == NinaEventType.FilterWheelChanged;
        }

        /// <summary>
        /// Timer event handler for periodic mount info refresh
        /// </summary>
        private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_disposed || !_ninaAvailable)
            {
                return;
            }

            try
            {
                // Check for capture timeout (safety mechanism in case WebSocket events fail)
                if (_isCapturing && _captureStartTime != default)
                {
                    var elapsed = DateTime.Now - _captureStartTime;
                    var expectedDuration = TimeSpan.FromSeconds(_exposureTime + 30); // Exposure + 30 seconds processing time
                    
                    if (elapsed > expectedDuration)
                    {
                        Logger.LogWarning("Capture timeout detected - Elapsed: {Elapsed}, Expected: {Expected}. Checking capture status...", 
                            elapsed, expectedDuration);
                        
                        // Try to get capture statistics to see if it completed
                        try
                        {
                            var statisticsResult = await NinaApiClient.GetCameraStatisticsAsync();
                            if (statisticsResult.IsSuccessful)
                            {
                                _lastImageStatistics = statisticsResult.Value;
                                Logger.LogInformation("Capture timeout recovery - Found completed image with statistics: Stars: {Stars}, HFR: {HFR:F2}", 
                                    _lastImageStatistics.Stars, _lastImageStatistics.HFR);
                                
                                _isCapturing = false;
                                _captureStartTime = default;
                                await InvokeAsync(StateHasChanged);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Error during capture timeout recovery");
                            _errorMessage = "Capture timeout - unable to determine status";
                            _isCapturing = false;
                            _captureStartTime = default;
                            await InvokeAsync(StateHasChanged);
                        }
                    }
                }

                if (!_isWebSocketConnected)
                {
                    // If WebSocket is not connected, refresh via REST API
                    Logger.LogTrace("Timer refresh: WebSocket not connected, using REST API");
                    await RefreshMountInfoAsync();
                    await RefreshCameraInfoAsync();
                }
                // If WebSocket is connected, the real-time events should handle updates
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during timer refresh");
            }
        }

        /// <summary>
        /// Refresh mount information via REST API
        /// </summary>
        private async Task RefreshMountInfoAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                return;
            }

            try
            {
                var mountResult = await NinaApiClient.GetMountInfoAsync();
                if (mountResult.IsSuccessful)
                {
                    _mountInfo = mountResult.Value;
                    _lastUpdated = DateTime.Now;
                    await InvokeAsync(StateHasChanged);
                }
                else if (mountResult.Error != null)
                {
                    Logger.LogWarning("Failed to refresh mount info: {Error}", mountResult.Error.Message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error refreshing mount information");
            }
        }

        /// <summary>
        /// Refresh camera information via REST API
        /// </summary>
        private async Task RefreshCameraInfoAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                return;
            }

            try
            {
                var cameraResult = await NinaApiClient.GetCameraInfoAsync();
                if (cameraResult.IsSuccessful)
                {
                    _cameraInfo = cameraResult.Value;
                    _isCameraConnected = _cameraInfo.Connected;
                    await InvokeAsync(StateHasChanged);
                }
                else if (cameraResult.Error != null)
                {
                    Logger.LogWarning("Failed to refresh camera info: {Error}", cameraResult.Error.Message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error refreshing camera information");
            }
        }

        /// <summary>
        /// Refresh filter wheel information via REST API
        /// </summary>
        private async Task RefreshFilterWheelInfoAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                return;
            }

            try
            {
                var filterWheelResult = await NinaApiClient.GetFilterWheelInfoAsync();
                if (filterWheelResult.IsSuccessful)
                {
                    _filterWheelInfo = filterWheelResult.Value;
                    _isFilterWheelConnected = _filterWheelInfo.Connected;
                    
                    // Update available filters
                    if (_filterWheelInfo.AvailableFilters != null)
                    {
                        _availableFilters = _filterWheelInfo.AvailableFilters.ToList();
                        
                        // Set default filter if none selected
                        if (string.IsNullOrEmpty(_selectedFilter) && _availableFilters.Count > 0)
                        {
                            _selectedFilter = _availableFilters.First().Name;
                        }

                        // Set default filter position if none selected
                        if (_selectedFilterPosition < 0 && _availableFilters.Count > 0)
                        {
                            _selectedFilterPosition = _availableFilters.First().Position;
                        }
                    }
                    
                    await InvokeAsync(StateHasChanged);
                }
                else if (filterWheelResult.Error != null)
                {
                    Logger.LogWarning("Failed to refresh filter wheel info: {Error}", filterWheelResult.Error.Message);
                }
            }
            catch (Exception ex)
            {
            Logger.LogError(ex, "Error refreshing filter wheel information");
            }
        }

        /// <summary>
        /// Load available camera devices for selection - with fallback for missing method
        /// </summary>
        private async Task LoadCameraDevicesAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                return;
            }

            try
            {
                Logger.LogDebug("Loading available camera devices");
                
                // Check if the method exists and handle gracefully if not
                try
                {
                    var devicesResult = await NinaApiClient.GetCameraDevicesAsync();
                    
                    if (devicesResult.IsSuccessful)
                    {
                        _availableCameraDevices = devicesResult.Value.ToList();
                        Logger.LogInformation("Loaded {Count} camera devices", _availableCameraDevices.Count);
                    }
                    else
                    {
                        Logger.LogWarning("Failed to load camera devices: {Error}", devicesResult.Error?.Message ?? "Unknown error");
                    }
                }
                catch (NotImplementedException)
                {
                    Logger.LogInformation("GetCameraDevicesAsync not implemented - using fallback");
                    _availableCameraDevices = new List<DeviceInfo>();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error loading camera devices - continuing without device list");
                _availableCameraDevices = new List<DeviceInfo>();
            }
        }

        /// <summary>
        /// Load data from the NINA API - wrapper for button click events
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task LoadDataButtonAsync()
        {
            await LoadDataAsync(forceRefresh: true);
        }

        /// <summary>
        /// Load data from the NINA API with improved error handling
        /// </summary>
        /// <param name="forceRefresh">Forces a refresh of data from the API, bypassing the cache</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task LoadDataAsync(bool forceRefresh = false)
        {
            if (_disposed)
            {
                Logger.LogWarning("Attempted to load data after component disposal");
                return;
            }

            _isLoading = true;
            _errorMessage = null;
            await InvokeAsync(StateHasChanged);

            try
            {
                Logger.LogDebug("Loading NINA version, mount, camera, and filter wheel information");

                // Load version information first to check if NINA is available
                try
                {
                    var versionResult = await NinaApiClient.GetVersionAsync();
                    if (versionResult.IsSuccessful)
                    {
                        _versionInfo = versionResult.Value;
                        _ninaAvailable = true;
                        Logger.LogInformation("Successfully loaded NINA version: {Version}", _versionInfo);
                    }
                    else
                    {
                        _ninaAvailable = false;
                        Logger.LogWarning("Failed to load NINA version - NINA may not be running: {Error}", versionResult.Error?.Message ?? "Unknown error");
                        _errorMessage = "NINA appears to be offline. Please ensure NINA is running with the Advanced API plugin enabled.";
                        return; // Don't try other endpoints if version fails
                    }
                }
                catch (HttpRequestException ex)
                {
                    _ninaAvailable = false;
                    Logger.LogWarning(ex, "HTTP connection failed - NINA server not reachable");
                    _errorMessage = "Cannot connect to NINA server. Please check that NINA is running and the Advanced API plugin is enabled.";
                    return;
                }
                catch (TaskCanceledException ex)
                {
                    _ninaAvailable = false;
                    Logger.LogWarning(ex, "Request timeout - NINA server not responding");
                    _errorMessage = "NINA server connection timeout. Please check network connectivity.";
                    return;
                }

                // Only proceed with other endpoints if NINA is available
                if (_ninaAvailable)
                {
                    // Load mount information
                    try
                    {
                        var mountResult = await NinaApiClient.GetMountInfoAsync();
                        if (mountResult.IsSuccessful)
                        {
                            _mountInfo = mountResult.Value;
                            Logger.LogInformation("Successfully loaded mount information - Connected: {Connected}, Name: {Name}", 
                                _mountInfo?.Connected, _mountInfo?.Name);
                        }
                        else
                        {
                            Logger.LogWarning("Failed to load mount information: {Error}", mountResult.Error?.Message ?? "Unknown error");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Error loading mount information - continuing without mount data");
                    }

                    // Load camera information
                    await RefreshCameraInfoAsync();

                    // Load filter wheel information
                    await RefreshFilterWheelInfoAsync();

                    // Load available camera devices for enhanced connection options
                    await LoadCameraDevicesAsync();
                }

                _lastUpdated = DateTime.Now;
                Logger.LogDebug("Data loading completed at {Timestamp}", _lastUpdated);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error occurred while loading NINA data");
                _errorMessage = $"Unexpected error: {ex.Message}";
                _ninaAvailable = false;
            }
            finally
            {
                _isLoading = false;
                if (!_disposed)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        /// <summary>
        /// Capture an image with the specified parameters
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task CaptureImageAsync()
        {
            if (_disposed || !_isCameraConnected || !_ninaAvailable)
            {
                Logger.LogWarning("Cannot capture image - camera not connected, NINA not available, or component disposed");
                _errorMessage = _ninaAvailable ? "Camera is not connected. Please connect the camera first." : "NINA is not available. Please ensure NINA is running.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            _isCapturing = true;
            _errorMessage = null;
            _lastImageStatistics = null;
            _capturedImageData = null; // Clear previous image
            await InvokeAsync(StateHasChanged);

            try
            {
                Logger.LogInformation("Starting image capture - Exposure: {Exposure}s, Filter: {Filter}", 
                    _exposureTime, _selectedFilter ?? "None");

                // Record the capture start time
                _captureStartTime = DateTime.Now;

                // Start the capture asynchronously - this will return immediately
                // We'll be notified via WebSocket when the capture is complete
                var captureResult = await NinaApiClient.CaptureAsync(duration: _exposureTime);
                
                if (captureResult.IsSuccessful)
                {
                    var captureWrapper = captureResult.Value;
                    
                    if (captureWrapper.IsStringResponse)
                    {
                        var captureStatus = captureWrapper.AsStringResponse;
                        Logger.LogInformation("Image capture started successfully - Status: {Status}. Waiting for WebSocket completion event...", captureStatus);
                        
                        // Record the capture start time for timeout handling
                        _captureStartTime = DateTime.Now;
                        
                        // The capture is now running asynchronously in NINA
                        // We'll get notified via WebSocket when it's finished (ApiCaptureFinished event)
                        // The _isCapturing flag will be cleared in HandleCaptureFinishedAsync()
                    }
                    else if (captureWrapper.IsCaptureResponse)
                    {
                        // This would happen if NINA returned the image immediately (very short exposures)
                        var captureData = captureWrapper.AsCaptureResponse;
                        Logger.LogInformation("Image capture completed immediately - HasImage: {HasImage}, HasPlatesolve: {HasPlatesolve}", 
                            !string.IsNullOrEmpty(captureData?.Image), captureData?.PlateSolveResult != null);
                        
                        // Store the captured image data if available
                        if (!string.IsNullOrEmpty(captureData?.Image))
                        {
                            _capturedImageData = captureData.Image;
                            Logger.LogInformation("Captured image data stored - Size: {Size} bytes", 
                                Convert.FromBase64String(_capturedImageData).Length);
                        }
                        
                        // Capture completed immediately
                        _isCapturing = false;
                        await InvokeAsync(StateHasChanged);
                    }
                    else
                    {
                        Logger.LogWarning("Unexpected capture response format");
                        _errorMessage = "Unexpected response format from NINA";
                        _isCapturing = false;
                    }
                }
                else
                {
                    Logger.LogError(captureResult.Error, "Image capture failed");
                    _errorMessage = $"Image capture failed: {captureResult.Error?.Message ?? "Unknown error"}";
                    _isCapturing = false;
                }

                _lastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during image capture");
                _errorMessage = $"Unexpected error during image capture: {ex.Message}";
                _isCapturing = false;
            }
            finally
            {
                if (!_disposed)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        /// <summary>
        /// Connect to the camera
        /// </summary>
        private async Task ConnectCameraAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                _errorMessage = "NINA is not available. Please ensure NINA is running.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            try
            {
                Logger.LogInformation("Connecting to camera");
                var result = await NinaApiClient.ConnectCameraAsync();
                
                if (result.IsSuccessful)
                {
                    Logger.LogInformation("Camera connected successfully");
                    await RefreshCameraInfoAsync();
                }
                else
                {
                    Logger.LogError(result.Error, "Failed to connect camera");
                    _errorMessage = $"Failed to connect camera: {result.Error?.Message ?? "Unknown error"}";
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error connecting camera");
                _errorMessage = $"Error connecting camera: {ex.Message}";
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Disconnect from the camera
        /// </summary>
        private async Task DisconnectCameraAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                return;
            }

            try
            {
                Logger.LogInformation("Disconnecting camera");
                var result = await NinaApiClient.DisconnectCameraAsync();
                
                if (result.IsSuccessful)
                {
                    Logger.LogInformation("Camera disconnected successfully");
                    await RefreshCameraInfoAsync();
                }
                else
                {
                    Logger.LogError(result.Error, "Failed to disconnect camera");
                    _errorMessage = $"Failed to disconnect camera: {result.Error?.Message ?? "Unknown error"}";
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error disconnecting camera");
                _errorMessage = $"Error disconnecting camera: {ex.Message}";
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Get screenshot from NINA application
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task GetScreenshotAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                Logger.LogWarning("Attempted to get screenshot when NINA not available or component disposed");
                _errorMessage = "NINA is not available. Please ensure NINA is running.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            _isScreenshotLoading = true;
            await InvokeAsync(StateHasChanged);

            try
            {
                Logger.LogDebug("Getting NINA screenshot");

                var screenshotResult = await NinaApiClient.TakeScreenshotAsync(cancellationToken: CancellationToken.None);
                if (screenshotResult.IsSuccessful)
                {
                    // Convert the string result to bytes (assuming it's base64)
                    if (!string.IsNullOrEmpty(screenshotResult.Value))
                    {
                        try
                        {
                            _screenshotData = Convert.FromBase64String(screenshotResult.Value);
                            Logger.LogInformation("Successfully retrieved NINA screenshot - Size: {Size} bytes", _screenshotData.Length);
                        }
                        catch (FormatException)
                        {
                            Logger.LogWarning("Screenshot result is not valid base64 format");
                            _errorMessage = "Screenshot data format error";
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Screenshot result was empty");
                        _errorMessage = "Empty screenshot data";
                    }
                }
                else
                {
                    Logger.LogError(screenshotResult.Error, "Failed to get NINA screenshot");
                    _errorMessage = $"Failed to get screenshot: {screenshotResult.Error?.Message ?? "Unknown error"}";
                }

                _lastUpdated = DateTime.Now;
                Logger.LogDebug("Screenshot loading completed at {Timestamp}", _lastUpdated);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error occurred while getting screenshot");
                _errorMessage = $"Unexpected error getting screenshot: {ex.Message}";
            }
            finally
            {
                _isScreenshotLoading = false;
                if (!_disposed)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        /// <summary>
        /// Open screenshot in full-size modal
        /// </summary>
        private void OpenScreenshotModal()
        {
            if (_screenshotData != null)
            {
                // Use JavaScript to open the screenshot in a new window
                var base64String = Convert.ToBase64String(_screenshotData);
                var dataUrl = $"data:image/png;base64,{base64String}";
                
                // Create a JavaScript function call to open the image in a new window
                var script = $"window.open('{dataUrl}', '_blank', 'width=800,height=600,scrollbars=yes,resizable=yes');";
                
                // Note: In a real implementation, you'd use IJSRuntime to execute this
                // For now, we'll log that the modal would open
                Logger.LogInformation("Opening screenshot modal - would execute: {Script}", script);
            }
        }

        /// <summary>
        /// Download screenshot as PNG file
        /// </summary>
        private void DownloadScreenshot()
        {
            if (_screenshotData != null)
            {
                // Create a download filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"NINA_Screenshot_{timestamp}.png";
                
                // Create a data URL and trigger download
                var base64String = Convert.ToBase64String(_screenshotData);
                var dataUrl = $"data:image/png;base64,{base64String}";
                
                // Note: In a real implementation, you'd use IJSRuntime to trigger the download
                // For now, we'll log the download action
                Logger.LogInformation("Downloading screenshot as {Filename} - Size: {Size} bytes", 
                    filename, _screenshotData.Length);
            }
        }

        /// <summary>
        /// Open captured image in full-size modal
        /// </summary>
        private void OpenCapturedImageModal()
        {
            if (!string.IsNullOrEmpty(_capturedImageData))
            {
                // Create a JavaScript function call to open the image in a new window
                var dataUrl = $"data:image/png;base64,{_capturedImageData}";
                var script = $"window.open('{dataUrl}', '_blank', 'width=800,height=600,scrollbars=yes,resizable=yes');";
                
                // Note: In a real implementation, you'd use IJSRuntime to execute this
                // For now, we'll log that the modal would open
                Logger.LogInformation("Opening captured image modal - would execute: {Script}", script);
            }
        }

        /// <summary>
        /// Download captured image as PNG file
        /// </summary>
        private void DownloadCapturedImage()
        {
            if (!string.IsNullOrEmpty(_capturedImageData))
            {
                // Create a download filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"Captured_Image_{timestamp}.png";
                
                // Create a data URL and trigger download
                var dataUrl = $"data:image/png;base64,{_capturedImageData}";
                
                // Note: In a real implementation, you'd use IJSRuntime to trigger the download
                // For now, we'll log the download action
                Logger.LogInformation("Downloading captured image as {Filename} - Size: {Size} bytes", 
                    filename, Convert.FromBase64String(_capturedImageData).Length);
            }
        }

        /// <summary>
        /// Test WebSocket connectivity
        /// </summary>
        private async Task TestWebSocketAsync()
        {
            if (_disposed || !_ninaAvailable)
            {
                Logger.LogWarning("Cannot test WebSocket - NINA not available or component disposed");
                _errorMessage = "NINA is not available. Please ensure NINA is running.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            try
            {
                Logger.LogInformation("Testing WebSocket connectivity...");
                
                // First, try to discover available endpoints
                var discoveryResult = await NinaWebSocketClient.DiscoverWebSocketEndpointAsync();
                if (discoveryResult.IsSuccessful)
                {
                    Logger.LogInformation("🎯 Available WebSocket endpoints: {Endpoints}", discoveryResult.Value);
                }
                else
                {
                    Logger.LogWarning("🔍 Endpoint discovery failed: {Error}", discoveryResult.Error?.Message);
                }
                
                // Check if we're connected
                if (!_isWebSocketConnected)
                {
                    Logger.LogWarning("WebSocket not connected, attempting to connect...");
                    await ConnectWebSocketAsync();
                }

                if (_isWebSocketConnected)
                {
                    // Send a test message
                    var result = await NinaWebSocketClient.SendTestMessageAsync();
                    if (result.IsSuccessful)
                    {
                        Logger.LogInformation("✅ WebSocket test message sent successfully");
                        _errorMessage = null;
                    }
                    else
                    {
                        Logger.LogError(result.Error, "❌ WebSocket test message failed");
                        _errorMessage = $"WebSocket test failed: {result.Error?.Message}";
                    }
                }
                else
                {
                    _errorMessage = "Failed to establish WebSocket connection. See discovery results above.";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error testing WebSocket");
                _errorMessage = $"WebSocket test error: {ex.Message}";
            }
            finally
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Dispose of component resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        /// <param name="disposing">True if disposing managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Logger.LogDebug("Disposing NINA demo component");
                _disposed = true;

                // Dispose timer
                _updateTimer?.Stop();
                _updateTimer?.Dispose();

                // Unsubscribe from WebSocket events
                try
                {
                    if (NinaWebSocketClient != null)
                    {
                        NinaWebSocketClient.EventReceived -= OnNinaEventReceived;
                        NinaWebSocketClient.ConnectionStateChanged -= OnWebSocketConnectionStateChanged;
                        NinaWebSocketClient.ErrorOccurred -= OnWebSocketErrorOccurred;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error unsubscribing from WebSocket events during disposal");
                }
            }
        }
    }
}
