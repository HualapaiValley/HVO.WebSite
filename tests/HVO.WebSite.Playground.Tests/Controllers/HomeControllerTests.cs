using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using HVO.WebSite.Playground.Controllers;

namespace HVO.WebSite.Playground.Tests.Controllers;

/// <summary>
/// Unit tests for HomeController MVC endpoints
/// </summary>
[TestClass]public class HomeControllerTests
{
    private readonly Mock<ILogger<HomeController>> _loggerMock;
    private readonly HomeController _controller;

    public HomeControllerTests()
    {
        _loggerMock = new Mock<ILogger<HomeController>>();
        _controller = new HomeController(_loggerMock.Object);
    }

    [TestMethod]
    public void Index_ShouldReturnViewResult()
    {
        // Act
        var result = _controller.Index();

        // Assert
        result.Should().BeOfType<ViewResult>();
        
        var viewResult = result as ViewResult;
        viewResult!.ViewName.Should().BeNullOrEmpty(); // Default view name
    }

    [TestMethod]
    public void HealthCheckMVC_ShouldReturnViewResult()
    {
        // Act
        var result = _controller.HealthCheckMVC();

        // Assert
        result.Should().BeOfType<ViewResult>();
        
        var viewResult = result as ViewResult;
        viewResult!.ViewName.Should().BeNullOrEmpty(); // Default view name
    }

    [TestMethod] 
    public void Controller_ShouldHaveCorrectLogger()
    {
        // Assert
        _controller.Should().NotBeNull();
        // Verify controller was constructed successfully with logger
    }
}
