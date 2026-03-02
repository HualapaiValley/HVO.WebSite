# HVO.NinaClient - NINA WebSocket & REST API Client

A comprehensive .NET client library for interacting with the NINA (Nighttime Imaging 'N' Astronomy) Advanced API, providing both WebSocket real-time events and REST API equipment control.

## Overview

This library provides dual interfaces to NINA:

### 🔌 WebSocket Client (`INinaWebSocketClient`)
Real-time event streaming and command interface supporting:
- **Socket Events**: Real-time notifications for equipment status, sequences, and system events
- **Mount Control**: Manual mount axis movement with safety controls
- **TPPA Integration**: Polar alignment automation using the TPPA plugin
- **Filter Wheel Control**: Networked filter wheel management

### 🌐 REST API Client (`INinaApiClient`)
Complete equipment control and imaging operations:
- **Equipment Information**: Camera, Mount, Filter Wheel, Focuser, Dome, Rotator, Guider, Weather, Safety Monitor, Switch, and Flat Device
- **Device Control**: Connection management and operational commands
- **Focuser Control**: Position control, autofocus operations, and focus curve analysis
- **Imaging Operations**: Image capture, star analysis, and plate solving
- **Flat Frame Capture**: Sky flats, auto-brightness flats, auto-exposure flats, and trained flat capture
- **Observatory Control**: Mount slewing, tracking, guiding, and safety monitoring

## Features

- ✅ **Comprehensive Coverage**: Full NINA API support including WebSocket events and REST endpoints
- ✅ **Type-Safe Models**: Strongly-typed models for all NINA responses with proper JSON serialization
- ✅ **Error Handling**: Uses HVO's `Result<T>` pattern for consistent error handling across all operations
- ✅ **Automatic Reconnection**: Robust WebSocket connection management with configurable retry logic
- ✅ **Thread-Safe**: Semaphore-based connection management for concurrent operations
- ✅ **Structured Logging**: Comprehensive logging using `ILogger<T>` with appropriate log levels
- ✅ **Dependency Injection**: Full support for Microsoft DI container with configuration binding
- ✅ **HTTP Client Integration**: Built on `IHttpClientFactory` for optimal performance and resource management

## Documentation

- [`docs/projects/nina-client/profile-api-usage.md`](../../docs/projects/nina-client/profile-api-usage.md) – reference workflows for profile management and equipment orchestration.
- [`docs/projects/nina-client/result-pattern-improvements.md`](../../docs/projects/nina-client/result-pattern-improvements.md) – guidelines for applying the `Result<T>` pattern to client operations.
- [`docs/projects/nina-client/resilience-architecture.md`](../../docs/projects/nina-client/resilience-architecture.md) – reconnection, retry, and circuit-breaker design notes.

## Installation

Add the project reference to your application:

```xml
<ProjectReference Include="..\HVO.NinaClient\HVO.NinaClient.csproj" />
```

## Configuration

### appsettings.json

```json
{
  "NinaApiClient": {
    "BaseUrl": "http://localhost:1888",
    "ApiKey": null,
    "TimeoutSeconds": 30,
    "MaxRetryAttempts": 3,
    "RetryDelayMs": 1000
  },
  "NinaWebSocket": {
    "BaseUri": "ws://localhost:1888/v2",
    "ConnectionTimeoutMs": 5000,
    "KeepAliveIntervalMs": 30000,
    "BufferSize": 4096,
    "MaxReconnectAttempts": 5,
    "ReconnectDelayMs": 2000
  }
}
```

### Service Registration

```csharp
// In Program.cs or Startup.cs
using HVO.NinaClient.Extensions;

// Register all NINA services (HTTP API + WebSocket)
services.AddNinaClient(configuration);

// Or register individually
services.AddNinaApiClient(configuration);
services.AddNinaWebSocketClient(configuration);

// Or with explicit configuration
services.AddNinaApiClient(options =>
{
    options.BaseUrl = "http://localhost:1888";
    options.TimeoutSeconds = 30;
});

services.AddNinaWebSocketClient(options =>
{
    options.BaseUri = "ws://localhost:1888/v2";
    options.MaxReconnectAttempts = 10;
});
```

## Usage Examples

### REST API - Equipment Control

```csharp
public class TelescopeController : ControllerBase
{
    private readonly INinaApiClient _ninaClient;
    private readonly ILogger<TelescopeController> _logger;

    public TelescopeController(INinaApiClient ninaClient, ILogger<TelescopeController> logger)
    {
        _ninaClient = ninaClient;
        _logger = logger;
    }

    [HttpGet("camera/info")]
    public async Task<IActionResult> GetCameraInfo()
    {
        var result = await _ninaClient.GetCameraInfoAsync();
        
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }
        
        _logger.LogError(result.Exception, "Failed to get camera information");
        return StatusCode(500, "Failed to retrieve camera information");
    }

    [HttpPost("capture")]
    public async Task<IActionResult> CaptureImage([FromBody] CaptureRequest request)
    {
        // Change filter if specified
        if (!string.IsNullOrEmpty(request.FilterName))
        {
            var filterResult = await _ninaClient.ChangeFilterAsync(request.FilterName);
            if (!filterResult.IsSuccess)
            {
                return BadRequest($"Failed to change filter: {filterResult.Exception.Message}");
            }
        }

        // Capture image
        var imageResult = await _ninaClient.CaptureImageAsync(request.ExposureTime, request.FilterName);
        if (!imageResult.IsSuccess)
        {
            return StatusCode(500, $"Image capture failed: {imageResult.Exception.Message}");
        }

        return Ok(imageResult.Value);
    }

    [HttpPost("mount/slew")]
    public async Task<IActionResult> SlewToCoordinates([FromBody] SlewRequest request)
    {
        var result = await _ninaClient.SlewToCoordinatesAsync(request.RightAscension, request.Declination);
        
        if (result.IsSuccess)
        {
            return Ok("Slew started successfully");
        }
        
        return StatusCode(500, $"Slew failed: {result.Exception.Message}");
    }
}

public record CaptureRequest(double ExposureTime, string? FilterName);
public record SlewRequest(double RightAscension, double Declination);
```

### REST API - Complex Imaging Sequence

```csharp
public class ImagingService
{
    private readonly INinaApiClient _ninaClient;
    private readonly ILogger<ImagingService> _logger;

    public ImagingService(INinaApiClient ninaClient, ILogger<ImagingService> logger)
    {
        _ninaClient = ninaClient;
        _logger = logger;
    }

    public async Task<Result<bool>> RunImagingSequenceAsync(string targetName, double ra, double dec)
    {
        _logger.LogInformation("Starting imaging sequence for {TargetName}", targetName);

        // 1. Connect all equipment
        var connectResult = await ConnectAllEquipmentAsync();
        if (!connectResult.IsSuccess)
        {
            return connectResult;
        }

        // 2. Slew to target
        var slewResult = await _ninaClient.SlewToCoordinatesAsync(ra, dec);
        if (!slewResult.IsSuccess)
        {
            return Result<bool>.Failure(slewResult.Exception);
        }

        // 3. Wait for slew completion
        await WaitForSlewCompletionAsync();

        // 4. Start guiding
        var guidingResult = await _ninaClient.StartGuidingAsync();
        if (!guidingResult.IsSuccess)
        {
            return Result<bool>.Failure(guidingResult.Exception);
        }

        // 5. Capture images in different filters
        string[] filters = { "Red", "Green", "Blue", "Luminance" };
        foreach (var filter in filters)
        {
            for (int i = 0; i < 5; i++) // 5 images per filter
            {
                var imageResult = await _ninaClient.CaptureImageAsync(60.0, filter);
                if (!imageResult.IsSuccess)
                {
                    _logger.LogError(imageResult.Exception, "Failed to capture image with {Filter} filter", filter);
                    continue;
                }

                // Dither between images
                if (i < 4) // Don't dither after last image
                {
                    await _ninaClient.DitherAsync(2.0);
                    await Task.Delay(5000); // Wait for settling
                }
            }
        }

        _logger.LogInformation("Imaging sequence completed for {TargetName}", targetName);
        return Result<bool>.Success(true);
    }

    private async Task<Result<bool>> ConnectAllEquipmentAsync()
    {
        var tasks = new[]
        {
            _ninaClient.ConnectCameraAsync(),
            _ninaClient.ConnectMountAsync(),
            _ninaClient.ConnectFilterWheelAsync(),
            _ninaClient.ConnectGuiderAsync()
        };

        var results = await Task.WhenAll(tasks);
        
        foreach (var result in results)
        {
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        return Result<bool>.Success(true);
    }

    private async Task WaitForSlewCompletionAsync()
    {
        while (true)
        {
            var mountInfo = await _ninaClient.GetMountInfoAsync();
            if (mountInfo.IsSuccess && !mountInfo.Value.Slewing)
            {
                break;
            }
            await Task.Delay(1000);
        }
    }
}
```

### WebSocket - Real-time Event Monitoring

```csharp
public class ObservatoryService

// Or register just the WebSocket client
services.AddNinaWebSocketClient(configuration);

// Or with explicit options
services.AddNinaWebSocketClient(new NinaWebSocketOptions
{
    BaseUri = "ws://localhost:1888/v2",
    ConnectionTimeoutMs = 5000,
    MaxReconnectAttempts = 3
});

// Or with configuration action
services.AddNinaWebSocketClient(options =>
{
    options.BaseUri = "ws://nina-server:1888/v2";
    options.MaxReconnectAttempts = 10;
});
```

## Usage Examples

### Basic Event Subscription

```csharp
public class ObservatoryService
{
    private readonly INinaWebSocketClient _ninaClient;
    private readonly ILogger<ObservatoryService> _logger;

    public ObservatoryService(INinaWebSocketClient ninaClient, ILogger<ObservatoryService> logger)
    {
        _ninaClient = ninaClient;
        _logger = logger;

        // Subscribe to events
        _ninaClient.EventReceived += OnNinaEventReceived;
        _ninaClient.ConnectionStateChanged += OnConnectionStateChanged;
        _ninaClient.ErrorOccurred += OnErrorOccurred;
    }

    public async Task StartMonitoringAsync()
    {
        var result = await _ninaClient.ConnectAsync();
        if (result.IsSuccessful)
        {
            _logger.LogInformation("Connected to NINA WebSocket server");
        }
        else
        {
            _logger.LogError("Failed to connect to NINA: {Error}", result.Error);
        }
    }

    private void OnNinaEventReceived(object? sender, NinaEventArgs e)
    {
        _logger.LogInformation("NINA Event: {EventType}", e.EventType);

        switch (e.EventType)
        {
            case NinaEventType.SequenceStarting:
                _logger.LogInformation("Imaging sequence starting");
                break;
                
            case NinaEventType.ImageSave:
                if (e.EventData is ImageStatistics stats)
                {
                    _logger.LogInformation("Image saved - HFR: {HFR}, Stars: {Stars}", stats.HFR, stats.Stars);
                }
                break;
                
            case NinaEventType.MountConnected:
                _logger.LogInformation("Mount connected");
                break;
                
            case NinaEventType.SafetyChanged:
                if (e.EventData is SafetyChangedResponse safety)
                {
                    _logger.LogInformation("Safety status changed: {IsSafe}", safety.IsSafe);
                }
                break;
        }
    }

    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        _logger.LogInformation("NINA connection state changed: {IsConnected}", isConnected);
    }

    private void OnErrorOccurred(object? sender, Exception error)
    {
        _logger.LogError(error, "NINA WebSocket error occurred");
    }
}
```

### Mount Control

```csharp
public class MountController
{
    private readonly INinaWebSocketClient _ninaClient;
    private readonly ILogger<MountController> _logger;

    public MountController(INinaWebSocketClient ninaClient, ILogger<MountController> logger)
    {
        _ninaClient = ninaClient;
        _logger = logger;
    }

    public async Task<Result<bool>> SlewEastAsync(double rate = 0.5)
    {
        var result = await _ninaClient.MoveMountAxisAsync(MountDirection.East, rate);
        if (result.IsSuccessful)
        {
            _logger.LogInformation("Mount slewing east at rate {Rate}", rate);
            
            // Note: Mount movement stops automatically after 2 seconds
            // Send command again to continue movement
        }
        
        return result;
    }

    public async Task<Result<bool>> CenterMountAsync()
    {
        // Stop all movement by simply not sending any more commands
        // Movement automatically stops after 2 seconds of inactivity
        _logger.LogInformation("Mount movement will stop automatically");
        return Result<bool>.Success(true);
    }
}
```

### TPPA Polar Alignment

```csharp
public class PolarAlignmentService
{
    private readonly INinaWebSocketClient _ninaClient;
    private readonly ILogger<PolarAlignmentService> _logger;

    public PolarAlignmentService(INinaWebSocketClient ninaClient, ILogger<PolarAlignmentService> logger)
    {
        _ninaClient = ninaClient;
        _logger = logger;
        
        // Subscribe to TPPA events
        _ninaClient.EventReceived += OnNinaEventReceived;
    }

    public async Task<Result<bool>> StartPolarAlignmentAsync()
    {
        var command = new TppaCommand
        {
            Action = "start-alignment",
            ManualMode = false,
            TargetDistance = 10,
            MoveRate = 1,
            EastDirection = true,
            StartFromCurrentPosition = true,
            ExposureTime = 5.0,
            Binning = 2,
            Gain = 100,
            Filter = "Luminance",
            AlignmentTolerance = 5.0
        };

        var result = await _ninaClient.StartTppaAlignmentAsync(command);
        if (result.IsSuccessful)
        {
            _logger.LogInformation("TPPA polar alignment started");
        }

        return result;
    }

    public async Task<Result<bool>> StopPolarAlignmentAsync()
    {
        return await _ninaClient.StopTppaAlignmentAsync();
    }

    private void OnNinaEventReceived(object? sender, NinaEventArgs e)
    {
        if (e.RawResponse?.Type == "Socket" && e.EventData is TppaAlignmentErrorResponse errorResponse)
        {
            _logger.LogInformation("TPPA Alignment Error - Az: {AzError}, Alt: {AltError}, Total: {TotalError}",
                errorResponse.AzimuthError, errorResponse.AltitudeError, errorResponse.TotalError);
        }
    }
}
```

### Filter Wheel Control

```csharp
public class FilterWheelController
{
    private readonly INinaWebSocketClient _ninaClient;
    private readonly ILogger<FilterWheelController> _logger;

    public FilterWheelController(INinaWebSocketClient ninaClient, ILogger<FilterWheelController> logger)
    {
        _ninaClient = ninaClient;
        _logger = logger;
        
        _ninaClient.EventReceived += OnNinaEventReceived;
    }

    public async Task<Result<bool>> GetCurrentFilterAsync()
    {
        return await _ninaClient.GetTargetFilterAsync();
    }

    public async Task<Result<bool>> SignalFilterChangeCompleteAsync()
    {
        return await _ninaClient.SignalFilterChangedAsync();
    }

    private void OnNinaEventReceived(object? sender, NinaEventArgs e)
    {
        switch (e.EventType)
        {
            case NinaEventType.FilterWheelChanged:
                if (e.EventData is FilterChangedResponse filterChange)
                {
                    _logger.LogInformation("Filter changed from {PreviousFilter} to {NewFilter}",
                        filterChange.Previous?.Name, filterChange.New?.Name);
                }
                break;
                
            case NinaEventType.FilterWheelConnected:
                _logger.LogInformation("Filter wheel connected");
                break;
                
            case NinaEventType.FilterWheelDisconnected:
                _logger.LogInformation("Filter wheel disconnected");
                break;
        }
    }
}
```

## Event Types

The client supports all NINA WebSocket events organized into categories:

### Equipment Events
- Camera: Connected, Disconnected, Download Timeout
- Mount: Connected, Disconnected, Parked, Homed, Before/After Flip
- Focuser: Connected, Disconnected, User Focused
- Filter Wheel: Connected, Disconnected, Changed
- Rotator: Connected, Disconnected, Moved, Synced
- Dome: Connected, Disconnected, Homed, Parked, Slewed, Shutter events
- Guider: Connected, Disconnected, Start, Stop, Dither

### Sequence Events
- Sequence Starting, Finished
- Sequence Entity Failed
- Advanced Sequence Start/Stop

### Imaging Events
- Image Save (with comprehensive statistics)
- API Capture Finished
- Autofocus Finished

### System Events
- Profile Added, Changed, Removed
- Safety Connected, Disconnected, Changed
- Weather Connected, Disconnected
- Error events (Autofocus, Platesolve)

### Target Scheduler Events
- Wait Start, Target Start, New Target Start
- Stack Updated (for live stacking)

## Architecture

### Models
- **NinaWebSocketOptions**: Configuration options for connection and behavior
- **NinaEventType**: Strongly-typed enumeration of all NINA events
- **Event Response Models**: Specific models for complex events (ImageStatistics, FilterChangedResponse, etc.)
- **Command Models**: Request models for mount control, TPPA commands, etc.

### Services
- **INinaWebSocketClient**: Main interface for WebSocket operations
- **NinaWebSocketClient**: Implementation with connection management and event processing

### Extensions
- **ServiceCollectionExtensions**: Dependency injection registration methods

## Error Handling

The client uses the HVO Result<T> pattern for consistent error handling:

```csharp
var result = await ninaClient.ConnectAsync();
if (result.IsSuccessful)
{
    // Success case
    var success = result.Value;
}
else
{
    // Error case
    var error = result.Error;
    logger.LogError(error, "Operation failed");
}
```

## Thread Safety

The client is designed to be thread-safe:
- Connection operations use semaphore-based locking
- Event processing runs on background threads
- Multiple commands can be sent concurrently

## Integration with HVOv9

This client integrates seamlessly with the HVOv9 observatory automation system:
- Uses HVO Result<T> pattern for consistent error handling
- Follows HVOv9 logging standards with structured logging
- Supports dependency injection patterns used throughout HVOv9
- Compatible with HVOv9 configuration management

## Requirements

- .NET 9.0+
- NINA with Advanced API plugin installed and configured
- Network connectivity to NINA WebSocket server (default: ws://localhost:1888/v2)

## Dependencies

- Microsoft.Extensions.Logging.Abstractions
- Microsoft.Extensions.DependencyInjection.Abstractions  
- Microsoft.Extensions.Options
- Microsoft.Extensions.Http
- Microsoft.Extensions.Configuration.Abstractions
- System.Text.Json
- HVO (for Result<T> pattern)

## License

Part of the HVOv9 (Hualapai Valley Observatory v9) project.
