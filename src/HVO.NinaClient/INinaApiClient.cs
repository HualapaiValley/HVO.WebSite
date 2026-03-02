using HVO.NinaClient.Models;
using HVO.Core.Results;

namespace HVO.NinaClient;

/// <summary>
/// Interface for NINA API client providing access to astronomy equipment and imaging operations
/// </summary>
public interface INinaApiClient
{
    // Application Methods (OpenAPI Compliant)
    Task<Result<string>> GetVersionAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> GetApplicationStartTimeAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> SwitchTabAsync(string tab, CancellationToken cancellationToken = default);
    Task<Result<string>> GetCurrentTabAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<string>>> GetInstalledPluginsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<LogEntry>>> GetApplicationLogsAsync(int lineCount, string? level = null, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EventEntry>>> GetEventHistoryAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> TakeScreenshotAsync(bool? resize = null, int? quality = null, string? size = null, double? scale = null, bool? stream = null, CancellationToken cancellationToken = default);

    // Camera Equipment Methods (NINA API Specification Compliant)
    Task<Result<CameraInfo>> GetCameraInfoAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> GetCameraDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanCameraDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> ConnectCameraAsync(string? deviceId = null, CancellationToken cancellationToken = default);
    Task<Result<string>> DisconnectCameraAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> SetCameraReadoutModeAsync(int mode, CancellationToken cancellationToken = default);
    Task<Result<string>> CoolCameraAsync(double temperature, double minutes, bool? cancel = null, CancellationToken cancellationToken = default);
    Task<Result<string>> WarmCameraAsync(double minutes, bool? cancel = null, CancellationToken cancellationToken = default);
    Task<Result<string>> SetCameraDewHeaterAsync(bool power, CancellationToken cancellationToken = default);
    Task<Result<string>> SetCameraBinningAsync(string binning, CancellationToken cancellationToken = default);
    Task<Result<CaptureResponseOrString>> CaptureAsync(
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
        CancellationToken cancellationToken = default);
    Task<Result<string>> AbortCameraExposureAsync(CancellationToken cancellationToken = default);
    Task<Result<ImageStatistics>> GetCameraStatisticsAsync(CancellationToken cancellationToken = default);

    // Dome Equipment Methods (NINA API Specification Compliant)
    Task<Result<DomeInfo>> GetDomeInfoAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> GetDomeDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanDomeDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> ConnectDomeAsync(string? deviceId = null, CancellationToken cancellationToken = default);
    Task<Result<string>> DisconnectDomeAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> OpenDomeShutterAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> CloseDomeShutterAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> StopDomeMovementAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> SetDomeFollowAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<Result<string>> SyncDomeToTelescopeAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> SlewDomeAsync(double azimuth, bool? waitToFinish = null, CancellationToken cancellationToken = default);
    Task<Result<string>> SetDomeParkPositionAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> ParkDomeAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> HomeDomeAsync(CancellationToken cancellationToken = default);

    // Filter Wheel Equipment Methods (NINA API Specification Compliant)
    Task<Result<FilterWheelInfo>> GetFilterWheelInfoAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> GetFilterWheelDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanFilterWheelDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> ConnectFilterWheelAsync(string? deviceId = null, CancellationToken cancellationToken = default);
    Task<Result<string>> DisconnectFilterWheelAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> ChangeFilterAsync(int filterId, CancellationToken cancellationToken = default);
    Task<Result<FilterInfo>> GetFilterInfoAsync(int filterId, CancellationToken cancellationToken = default);

    // Flat Device Equipment Methods (NINA API Specification Compliant)
    Task<Result<FlatDeviceInfo>> GetFlatDeviceInfoAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> GetFlatDeviceDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanFlatDeviceDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> ConnectFlatDeviceAsync(string? deviceId = null, CancellationToken cancellationToken = default);
    Task<Result<string>> DisconnectFlatDeviceAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> SetFlatDeviceLightAsync(bool on, CancellationToken cancellationToken = default);
    Task<Result<string>> SetFlatDeviceCoverAsync(bool closed, CancellationToken cancellationToken = default);
    Task<Result<string>> SetFlatDeviceBrightnessAsync(int brightness, CancellationToken cancellationToken = default);

    // Focuser Equipment Methods (NINA API Specification Compliant)
    Task<Result<FocuserInfo>> GetFocuserInfoAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> GetFocuserDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanFocuserDevicesAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> ConnectFocuserAsync(string? deviceId = null, CancellationToken cancellationToken = default);
    Task<Result<string>> DisconnectFocuserAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> MoveFocuserAsync(int position, CancellationToken cancellationToken = default);
    Task<Result<string>> AutoFocusAsync(bool? cancel = null, CancellationToken cancellationToken = default);
    Task<Result<FocuserLastAF>> GetLastAutoFocusAsync(CancellationToken cancellationToken = default);

    // Flat Capture Methods (NINA API Specification Compliant)
    Task<Result<string>> CaptureSkyFlatsAsync(
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
        CancellationToken cancellationToken = default);

    Task<Result<string>> CaptureAutoBrightnessFlatsAsync(
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
        CancellationToken cancellationToken = default);

    Task<Result<string>> CaptureAutoExposureFlatsAsync(
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
        CancellationToken cancellationToken = default);

    Task<Result<string>> CaptureTrainedDarkFlatsAsync(
        int count,
        int? filterId = null,
        string? binning = null,
        int? gain = null,
        int? offset = null,
        bool? keepClosed = null,
        CancellationToken cancellationToken = default);

    Task<Result<string>> CaptureTrainedFlatsAsync(
        int count,
        int? filterId = null,
        string? binning = null,
        int? gain = null,
        int? offset = null,
        bool? keepClosed = null,
        CancellationToken cancellationToken = default);

    Task<Result<FlatCaptureStatus>> GetFlatCaptureStatusAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> StopFlatCaptureAsync(CancellationToken cancellationToken = default);

    // Framing Assistant Methods (NINA API Specification Compliant)
    Task<Result<FramingAssistantInfo>> GetFramingAssistantInfoAsync(CancellationToken cancellationToken = default);
    Task<Result<string>> SetFramingAssistantSourceAsync(string source, CancellationToken cancellationToken = default);
    Task<Result<string>> SetFramingAssistantCoordinatesAsync(double rightAscension, double declination, CancellationToken cancellationToken = default);
    Task<Result<string>> SlewFramingAssistantAsync(string? option = null, CancellationToken cancellationToken = default);
    Task<Result<string>> SetFramingAssistantRotationAsync(double rotation, CancellationToken cancellationToken = default);
    Task<Result<string>> DetermineFramingAssistantRotationAsync(CancellationToken cancellationToken = default);

    // Guider Equipment Methods (NINA API Specification Compliant)
    // https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/api_spec.yaml#L1400

    /// <summary>
    /// Get guider equipment information
    /// </summary>
    /// <returns>Guider equipment information result</returns>
    Task<Result<GuiderInfoResponse>> GetGuiderInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// List available guider devices
    /// </summary>
    /// <returns>Available guider devices result</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> GetGuiderDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescan for guider devices
    /// </summary>
    /// <returns>Updated list of available guider devices result</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanGuiderDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to guider device
    /// </summary>
    /// <param name="to">Device identifier to connect to (optional)</param>
    /// <returns>Connection result</returns>
    Task<Result<string>> ConnectGuiderAsync(string? to = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from guider device
    /// </summary>
    /// <returns>Disconnection result</returns>
    Task<Result<string>> DisconnectGuiderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Start guiding with optional calibration
    /// </summary>
    /// <param name="calibrate">Whether to calibrate before starting guiding (optional)</param>  
    /// <returns>Start guiding result</returns>
    Task<Result<string>> StartGuidingAsync(bool? calibrate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop guiding
    /// </summary>
    /// <returns>Stop guiding result</returns>
    Task<Result<string>> StopGuidingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear guider calibration
    /// </summary>
    /// <returns>Clear calibration result</returns>
    Task<Result<string>> ClearGuiderCalibrationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get guiding graph history (guide steps)
    /// </summary>
    /// <returns>Guide steps history result with RMS statistics</returns>
    Task<Result<GuideStepsHistory>> GetGuiderGraphAsync(CancellationToken cancellationToken = default);

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
    Task<Result<ImageResponse>> GetImageAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets image history. Only one parameter is required
    /// </summary>
    /// <param name="all">Whether to get all images or only the current image</param>
    /// <param name="index">The index of the image to get</param>
    /// <param name="count">Whether to count the number of images</param>
    /// <param name="imageType">Filter by image type. This will restrict the result to images of the specified type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing image history data or count</returns>
    Task<Result<ImageHistoryResponse>> GetImageHistoryAsync(
        bool? all = null,
        int? index = null,
        bool? count = null,
        ImageType? imageType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the thumbnail of an image. This requires Create Thumbnails to be enabled in NINA.
    /// This thumbnail has a width of 256px.
    /// </summary>
    /// <param name="index">The index of the image to get</param>
    /// <param name="imageType">Filter the image history by image type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing thumbnail data</returns>
    Task<Result<byte[]>> GetImageThumbnailAsync(
        int index,
        ImageType? imageType = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Mount Equipment Methods

    /// <summary>
    /// Gets information about the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing mount information</returns>
    Task<Result<MountInfo>> GetMountInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets list of available mount devices
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available mount devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> GetMountDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescans for new mount devices and returns updated list
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available mount devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanMountDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to a mount device
    /// </summary>
    /// <param name="deviceId">The ID of the mount device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    Task<Result<string>> ConnectMountAsync(string? deviceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the currently connected mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    Task<Result<string>> DisconnectMountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Homes the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the home operation status</returns>
    Task<Result<string>> HomeMountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the mount tracking mode
    /// 0: Sidereal, 1: Lunar, 2: Solar, 3: King, 4: Stopped
    /// </summary>
    /// <param name="mode">The tracking mode to set (0-4)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the tracking mode operation status</returns>
    Task<Result<string>> SetMountTrackingModeAsync(int mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parks the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the park operation status</returns>
    Task<Result<string>> ParkMountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unparks the mount
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the unpark operation status</returns>
    Task<Result<string>> UnparkMountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a meridian flip to the current coordinates. This will only flip the mount if it is needed,
    /// it will not force the mount to flip
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the flip operation status</returns>
    Task<Result<string>> FlipMountAsync(CancellationToken cancellationToken = default);

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
    Task<Result<string>> SlewMountAsync(
        double ra,
        double dec,
        bool? waitForResult = null,
        bool? center = null,
        bool? rotate = null,
        double? rotationAngle = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops any slew, even if it was not started using the API. However this is mainly useful for slews you issued
    /// yourself, since it doesn't completely abort slew&centers started by NINA. Therefore the recommended use is
    /// to stop simple slews without center or rotate. With center or rotate, this may take a few seconds to complete.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop slew operation status</returns>
    Task<Result<string>> StopMountSlewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the current mount position as park position. This requires the mount to be unparked.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set park position operation status</returns>
    Task<Result<string>> SetMountParkPositionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync the scope, either by manually supplying the coordinates or by solving and syncing.
    /// If the coordinates are omitted, a platesolve will be performed.
    /// </summary>
    /// <param name="ra">Right ascension in degrees (optional)</param>
    /// <param name="dec">Declination in degrees (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the sync operation status</returns>
    Task<Result<string>> SyncMountAsync(double? ra = null, double? dec = null, CancellationToken cancellationToken = default);

    #endregion

    #region Rotator Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected rotator.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the rotator information</returns>
    Task<Result<RotatorInfoResponse>> GetRotatorInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to the selected rotator device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status</returns>
    Task<Result<string>> ConnectRotatorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the currently connected rotator device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status</returns>
    Task<Result<string>> DisconnectRotatorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a list of all available rotator devices that can be connected to.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available rotator devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> GetRotatorDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescan for available rotator devices and update the device list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available rotator devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanRotatorDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Move the rotator to the specified position in degrees.
    /// </summary>
    /// <param name="position">Target position in degrees (0-360)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the move operation status</returns>
    Task<Result<string>> MoveRotatorAsync(double position, CancellationToken cancellationToken = default);

    /// <summary>
    /// Move the rotator to the specified mechanical position in degrees.
    /// </summary>
    /// <param name="position">Target mechanical position in degrees</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the mechanical move operation status</returns>
    Task<Result<string>> MoveRotatorMechanicalAsync(double position, CancellationToken cancellationToken = default);

    #endregion

    #region Safety Monitor Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected safety monitor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the safety monitor information</returns>
    Task<Result<SafetyMonitorInfoResponse>> GetSafetyMonitorInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to the selected safety monitor device.
    /// </summary>
    /// <param name="deviceId">The ID of the safety monitor device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    Task<Result<string>> ConnectSafetyMonitorAsync(string? deviceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the currently connected safety monitor device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    Task<Result<string>> DisconnectSafetyMonitorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a list of all available safety monitor devices that can be connected to.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available safety monitor devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> GetSafetyMonitorDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescan for available safety monitor devices and update the device list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available safety monitor devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanSafetyMonitorDevicesAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Switch Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected switch.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the switch information</returns>
    Task<Result<SwitchInfoResponse>> GetSwitchInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to the selected switch device.
    /// </summary>
    /// <param name="deviceId">The ID of the switch device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    Task<Result<string>> ConnectSwitchAsync(string? deviceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the currently connected switch device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    Task<Result<string>> DisconnectSwitchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a list of all available switch devices that can be connected to.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available switch devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> GetSwitchDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescan for available switch devices and update the device list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available switch devices</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanSwitchDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the value of a switch at the specified index.
    /// </summary>
    /// <param name="index">The index of the switch to set</param>
    /// <param name="value">The value to set</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the set value operation status</returns>
    Task<Result<string>> SetSwitchValueAsync(int index, double value, CancellationToken cancellationToken = default);

    #endregion

    #region Weather Equipment Methods

    /// <summary>
    /// Get detailed information about the currently connected weather device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the weather information</returns>
    Task<Result<WeatherInfoResponse>> GetWeatherInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to the selected weather device.
    /// </summary>
    /// <param name="deviceId">The ID of the weather device to connect to (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the connection status message</returns>
    Task<Result<string>> ConnectWeatherAsync(string? deviceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the currently connected weather device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the disconnection status message</returns>
    Task<Result<string>> DisconnectWeatherAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a list of all available weather sources that can be connected to.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available weather sources</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> GetWeatherDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescan for available weather sources and update the device list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the updated list of available weather sources</returns>
    Task<Result<IReadOnlyList<DeviceInfo>>> RescanWeatherDevicesAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Livestack Methods

    /// <summary>
    /// Starts Livestack, requires Livestack >= v1.0.0.9. Note that this method cannot fail, 
    /// even if the livestack plugin is not installed or something went wrong. 
    /// This simply issues a command to start the livestack.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the start livestack operation status</returns>
    Task<Result<string>> StartLivestackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops Livestack, requires Livestack >= v1.0.0.9. Note that this method cannot fail, 
    /// even if the livestack plugin is not installed or something went wrong. 
    /// This simply issues a command to stop the livestack.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop livestack operation status</returns>
    Task<Result<string>> StopLivestackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the stacked image from the livestack plugin for a given target and filter.
    /// </summary>
    /// <param name="target">The target name (e.g., "M31")</param>
    /// <param name="filter">The filter name (e.g., "RGB")</param>
    /// <param name="resize">Whether to resize the image</param>
    /// <param name="quality">The quality of the image, ranging from 1 (worst) to 100 (best). -1 or omitted for png</param>
    /// <param name="size">The size of the image ([width]x[height]). Requires resize to be true</param>
    /// <param name="scale">The scale of the image. Requires resize to be true</param>
    /// <param name="stream">Stream the image to the client. This will stream the image in image/jpg or image/png format</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the base64 encoded image data</returns>
    Task<Result<string>> GetLivestackImageAsync(
        string target,
        string filter,
        bool? resize = null,
        int? quality = null,
        string? size = null,
        double? scale = null,
        bool? stream = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Sequence Methods

    /// <summary>
    /// Get sequence as JSON. For this to work, there needs to be a deep sky object container 
    /// present and the sequencer view has to be initialized. This endpoint is generally 
    /// advised to use over state since it gives more reliable results.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the sequence as JSON</returns>
    Task<Result<SequenceOrGlobalTriggers>> GetSequenceJsonAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get complete sequence as JSON. For this to work, there needs to be a deep sky object 
    /// container present and the sequencer view has to be initialized. This is similar to 
    /// the json endpoint, however the returned sequence is much more elaborate and also 
    /// supports plugins. Use this endpoint (not json!) as reference for sequence editing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the complete sequence state</returns>
    Task<Result<SequenceOrGlobalTriggers>> GetSequenceStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a sequence. This works similarly to profile/change-value. Note that this mainly 
    /// supports fields that expect simple types like strings, numbers etc, and may not work 
    /// for things like enums or objects (filter, time source, ...). Use 'sequence/state' 
    /// as reference, not 'sequence/json'.
    /// </summary>
    /// <param name="path">The path to the property that should be updated. Use `GlobalTriggers`, `Start`, `Imaging`, `End` for the sequence root containers. Then use the name of the property or the index of the item in a list, separated with `-`.</param>
    /// <param name="value">The new value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the edit operation status</returns>
    Task<Result<string>> EditSequenceAsync(string path, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start sequence. This requires the sequencer to be initialized, which can be achieved 
    /// by opening the tab once.
    /// </summary>
    /// <param name="skipValidation">Skip validation of the sequence</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the start operation status</returns>
    Task<Result<string>> StartSequenceAsync(bool? skipValidation = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop sequence. This requires the sequencer to be initialized, which can be achieved 
    /// by opening the tab once.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the stop operation status</returns>
    Task<Result<string>> StopSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset sequence. This requires the sequencer to be initialized, which can be achieved 
    /// by opening the tab once.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the reset operation status</returns>
    Task<Result<string>> ResetSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// List available sequences. This is currently not really useful as it is not possible 
    /// to load sequences.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the list of available sequences</returns>
    Task<Result<AvailableSequencesResponse>> ListAvailableSequencesAsync(CancellationToken cancellationToken = default);

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
    Task<Result<string>> SetSequenceTargetAsync(
        string name, 
        double ra, 
        double dec, 
        double rotation, 
        int index, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load a sequence from a file from the default sequence folders. The names can be 
    /// retrieved using the ListAvailableSequences endpoint.
    /// </summary>
    /// <param name="sequenceName">The name of the sequence to load</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the load operation status</returns>
    Task<Result<string>> LoadSequenceFromFileAsync(string sequenceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load a sequence from JSON supplied by the client.
    /// </summary>
    /// <param name="sequenceJson">The sequence JSON data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the load operation status</returns>
    Task<Result<string>> LoadSequenceFromJsonAsync(string sequenceJson, CancellationToken cancellationToken = default);

    #endregion

    #region Profile Methods

    /// <summary>
    /// Shows the profile information
    /// </summary>
    /// <param name="active">Whether to show the active profile or a list of all available profiles</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing profile information or list of profiles</returns>
    Task<Result<ProfileResponse>> ShowProfileAsync(bool? active = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a value in the profile
    /// </summary>
    /// <param name="settingPath">The path to the setting to change (e.g., "CameraSettings-PixelSize"). This refers to the profile structure like it is received when using ShowProfileAsync. Separate each object with a dash (-)</param>
    /// <param name="newValue">The new value to set</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the operation status</returns>
    Task<Result<string>> ChangeProfileValueAsync(string settingPath, object newValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the profile
    /// </summary>
    /// <param name="profileId">The ID of the profile to switch to. This ID can be retrieved using ShowProfileAsync with active=false</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the switch operation status</returns>
    Task<Result<string>> SwitchProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the horizon for the active profile
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result containing the horizon data with altitudes and azimuths</returns>
    Task<Result<HorizonData>> GetProfileHorizonAsync(CancellationToken cancellationToken = default);

    #endregion
}
