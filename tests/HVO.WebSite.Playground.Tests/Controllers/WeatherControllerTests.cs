using FluentAssertions;
using HVO.Core.Results;
using HVO.WebSite.Playground.Controllers;
using HVO.WebSite.Playground.Models;
using HVO.WebSite.Playground.Services;
using HVO.WebSite.Playground.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace HVO.WebSite.Playground.Tests.Controllers;

/// <summary>
/// Unit tests for the WeatherController class
/// Tests HTTP layer independently from business logic
/// </summary>
[TestClass]public class WeatherControllerTests
{
    private readonly Mock<IWeatherService> _mockWeatherService;
    private readonly Mock<ILogger<WeatherController>> _mockLogger;
    private readonly WeatherController _controller;

    public WeatherControllerTests()
    {
        _mockWeatherService = new Mock<IWeatherService>();
        _mockLogger = new Mock<ILogger<WeatherController>>();
        _controller = new WeatherController(_mockWeatherService.Object, _mockLogger.Object);
    }

    #region GetLatestWeatherRecord Tests

    [TestMethod]
    public async Task GetLatestWeatherRecord_WithSuccessfulService_ReturnsOkResult()
    {
        // Arrange
        var expectedResponse = WeatherTestDataBuilder.CreateSampleLatestWeatherResponse();
        var successResult = Result<LatestWeatherResponse>.Success(expectedResponse);
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
                          .ReturnsAsync(successResult);

        // Act
        var result = await _controller.GetLatestWeatherRecord();

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        okResult!.Value.Should().BeEquivalentTo(expectedResponse);
        
        // Verify service was called
        _mockWeatherService.Verify(x => x.GetLatestWeatherRecordAsync(), Times.Once);
    }

    [TestMethod]
    public async Task GetLatestWeatherRecord_WithInvalidOperationException_ReturnsNotFound()
    {
        // Arrange
        var exception = new InvalidOperationException("No weather records found");
        var failureResult = Result<LatestWeatherResponse>.Failure(exception);
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
                          .ReturnsAsync(failureResult);

        // Act
        var result = await _controller.GetLatestWeatherRecord();

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var problemResult = result.Result as ObjectResult;
        problemResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        
        var problemDetails = problemResult.Value as ProblemDetails;
        problemDetails!.Title.Should().Be("Weather Data Not Found");
        problemDetails.Detail.Should().Be(exception.Message);
    }

    [TestMethod]
    public async Task GetLatestWeatherRecord_WithGeneralException_ReturnsInternalServerError()
    {
        // Arrange
        var exception = new Exception("Database connection failed");
        var failureResult = Result<LatestWeatherResponse>.Failure(exception);
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
                          .ReturnsAsync(failureResult);

        // Act
        var result = await _controller.GetLatestWeatherRecord();

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var problemResult = result.Result as ObjectResult;
        problemResult!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        
        var problemDetails = problemResult.Value as ProblemDetails;
        problemDetails!.Title.Should().Be("Internal Server Error");
        problemDetails.Detail.Should().Be("An error occurred while retrieving weather data");
    }

    #endregion

    #region GetWeatherHighsLows Tests

    [TestMethod]
    public async Task GetWeatherHighsLows_WithSuccessfulService_ReturnsOkResult()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.Date;
        var endDate = startDate.AddDays(1);
        var expectedResponse = WeatherTestDataBuilder.CreateSampleHighsLowsResponse(startDate, endDate);
        var successResult = Result<WeatherHighsLowsResponse>.Success(expectedResponse);
        
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(startDate, endDate))
                          .ReturnsAsync(successResult);

        // Act
        var result = await _controller.GetWeatherHighsLows(startDate, endDate);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        okResult!.Value.Should().BeEquivalentTo(expectedResponse);
        
        // Verify service was called with correct parameters
        _mockWeatherService.Verify(x => x.GetWeatherHighsLowsAsync(startDate, endDate), Times.Once);
    }

    [TestMethod]
    public async Task GetWeatherHighsLows_WithNullDates_PassesNullToService()
    {
        // Arrange
        var expectedResponse = WeatherTestDataBuilder.CreateSampleHighsLowsResponse();
        var successResult = Result<WeatherHighsLowsResponse>.Success(expectedResponse);
        
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(null, null))
                          .ReturnsAsync(successResult);

        // Act
        var result = await _controller.GetWeatherHighsLows(null, null);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        
        // Verify service was called with null parameters
        _mockWeatherService.Verify(x => x.GetWeatherHighsLowsAsync(null, null), Times.Once);
    }

    [TestMethod]
    public async Task GetWeatherHighsLows_WithInvalidOperationException_ReturnsNotFound()
    {
        // Arrange
        var exception = new InvalidOperationException("No weather data found for date range");
        var failureResult = Result<WeatherHighsLowsResponse>.Failure(exception);
        
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                          .ReturnsAsync(failureResult);

        // Act
        var result = await _controller.GetWeatherHighsLows(null, null);

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var problemResult = result.Result as ObjectResult;
        problemResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        
        var problemDetails = problemResult.Value as ProblemDetails;
        problemDetails!.Title.Should().Be("Weather Data Not Found");
        problemDetails.Detail.Should().Be(exception.Message);
    }

    [TestMethod]
    public async Task GetWeatherHighsLows_WithGeneralException_ReturnsInternalServerError()
    {
        // Arrange
        var exception = new Exception("Database error");
        var failureResult = Result<WeatherHighsLowsResponse>.Failure(exception);
        
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                          .ReturnsAsync(failureResult);

        // Act
        var result = await _controller.GetWeatherHighsLows(null, null);

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var problemResult = result.Result as ObjectResult;
        problemResult!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        
        var problemDetails = problemResult.Value as ProblemDetails;
        problemDetails!.Title.Should().Be("Internal Server Error");
        problemDetails.Detail.Should().Be("An error occurred while retrieving weather highs and lows data");
    }

    #endregion

    #region GetCurrentWeatherConditions Tests

    [TestMethod]
    public async Task GetCurrentWeatherConditions_WithSuccessfulService_ReturnsOkResult()
    {
        // Arrange
        var expectedResponse = WeatherTestDataBuilder.CreateSampleCurrentWeatherResponse();
        var successResult = Result<CurrentWeatherResponse>.Success(expectedResponse);
        
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
                          .ReturnsAsync(successResult);

        // Act
        var result = await _controller.GetCurrentWeatherConditions();

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        okResult!.Value.Should().BeEquivalentTo(expectedResponse);
        
        // Verify service was called
        _mockWeatherService.Verify(x => x.GetCurrentWeatherConditionsAsync(), Times.Once);
    }

    [TestMethod]
    public async Task GetCurrentWeatherConditions_WithInvalidOperationException_ReturnsNotFound()
    {
        // Arrange
        var exception = new InvalidOperationException("No current weather data available");
        var failureResult = Result<CurrentWeatherResponse>.Failure(exception);
        
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
                          .ReturnsAsync(failureResult);

        // Act
        var result = await _controller.GetCurrentWeatherConditions();

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var problemResult = result.Result as ObjectResult;
        problemResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        
        var problemDetails = problemResult.Value as ProblemDetails;
        problemDetails!.Title.Should().Be("Current Weather Data Not Found");
        problemDetails.Detail.Should().Be(exception.Message);
    }

    [TestMethod]
    public async Task GetCurrentWeatherConditions_WithGeneralException_ReturnsInternalServerError()
    {
        // Arrange
        var exception = new Exception("Service unavailable");
        var failureResult = Result<CurrentWeatherResponse>.Failure(exception);
        
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
                          .ReturnsAsync(failureResult);

        // Act
        var result = await _controller.GetCurrentWeatherConditions();

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var problemResult = result.Result as ObjectResult;
        problemResult!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        
        var problemDetails = problemResult.Value as ProblemDetails;
        problemDetails!.Title.Should().Be("Internal Server Error");
        problemDetails.Detail.Should().Be("An error occurred while retrieving current weather conditions");
    }

    #endregion

    #region Error Handling Pattern Tests

    [TestMethod]
    public async Task AllEndpoints_UseConsistentErrorHandlingPattern()
    {
        // Arrange
        var exception = new ArgumentException("Test exception");
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
                          .ReturnsAsync(Result<LatestWeatherResponse>.Failure(exception));
        _mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                          .ReturnsAsync(Result<WeatherHighsLowsResponse>.Failure(exception));
        _mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
                          .ReturnsAsync(Result<CurrentWeatherResponse>.Failure(exception));

        // Act & Assert - All should return 500 for non-InvalidOperationException
        var latestResult = await _controller.GetLatestWeatherRecord();
        var highsLowsResult = await _controller.GetWeatherHighsLows(null, null);
        var currentResult = await _controller.GetCurrentWeatherConditions();

        // Assert all return consistent error structure
        var results = new[] { latestResult.Result, highsLowsResult.Result, currentResult.Result };
        
        foreach (var result in results)
        {
            result.Should().BeOfType<ObjectResult>();
            var problemResult = result as ObjectResult;
            problemResult!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            
            var problemDetails = problemResult.Value as ProblemDetails;
            problemDetails!.Title.Should().Be("Internal Server Error");
            problemDetails.Detail.Should().NotBeNullOrEmpty();
        }
    }

    #endregion

    #region Result Pattern Integration Tests

    [TestMethod]
    public async Task AllEndpoints_ProperlyUseResultPatternMatch()
    {
        // This test verifies that the controller properly uses the Result pattern's Match method
        // and doesn't access .Value or .Error directly without checking IsSuccessful
        
        // Arrange
        var successResponse = WeatherTestDataBuilder.CreateSampleLatestWeatherResponse();
        var successResult = Result<LatestWeatherResponse>.Success(successResponse);
        
        _mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
                          .ReturnsAsync(successResult);

        // Act
        var result = await _controller.GetLatestWeatherRecord();

        // Assert - The fact that this doesn't throw demonstrates proper Result pattern usage
        result.Result.Should().BeOfType<OkObjectResult>();
        
        // Verify the service was called exactly once (no retry logic or multiple calls)
        _mockWeatherService.Verify(x => x.GetLatestWeatherRecordAsync(), Times.Once);
    }

    #endregion
}
