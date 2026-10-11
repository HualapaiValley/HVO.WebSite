using FluentAssertions;
using HVO.Core.Results;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class WeatherControllerTests
{
    [TestMethod]
    public async Task GetLatestWeatherRecord_ReturnsOk_WhenServiceSucceeds()
    {
        var response = new LatestWeatherResponse
        {
            Timestamp = DateTime.UtcNow,
            MachineName = "unit-test"
        };

        var weatherService = new Mock<IWeatherService>();
        weatherService
            .Setup(service => service.GetLatestWeatherRecordAsync())
            .ReturnsAsync(Result<LatestWeatherResponse>.Success(response));

        var controller = new WeatherController(weatherService.Object, NullLogger<WeatherController>.Instance);

        var action = await controller.GetLatestWeatherRecord();

        var okResult = action.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);

        var payload = okResult.Value as LatestWeatherResponse;
        payload.Should().NotBeNull();
        payload!.MachineName.Should().Be("unit-test");
    }

    [TestMethod]
    public async Task GetLatestWeatherRecord_ReturnsNotFound_WhenServiceHasNoData()
    {
        var weatherService = new Mock<IWeatherService>();
        weatherService
            .Setup(service => service.GetLatestWeatherRecordAsync())
            .ReturnsAsync(new InvalidOperationException("No weather records found"));

        var controller = new WeatherController(weatherService.Object, NullLogger<WeatherController>.Instance);

        var action = await controller.GetLatestWeatherRecord();

        var objectResult = action.Result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var details = objectResult.Value as ProblemDetails;
        details.Should().NotBeNull();
        details!.Title.Should().Be("Weather Data Not Found");
    }

    [TestMethod]
    public async Task HighsLows_DatabaseFailureReturns500WithSanitizedDetails()
    {
        var service = new Mock<IWeatherService>();
        service.Setup(value => value.GetWeatherHighsLowsAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvalidOperationException("Private provider failure"));
        var controller = new WeatherController(service.Object, NullLogger<WeatherController>.Instance);
        var action = await controller.GetWeatherHighsLows();
        var result = action.Result.Should().BeOfType<ObjectResult>().Which;
        result.StatusCode.Should().Be(500);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Detail.Should().NotContain("Private provider failure");
    }
}
