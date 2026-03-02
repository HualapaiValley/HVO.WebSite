using FluentAssertions;
using HVO.WebSite.Playground.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.WebSite.Playground.Tests.Middleware;

/// <summary>
/// Unit tests for HvoServiceExceptionHandler middleware
/// </summary>
[TestClass]public class HvoServiceExceptionHandlerTests
{
    private readonly Mock<IProblemDetailsService> _mockProblemDetailsService;
    private readonly Mock<ILogger<HvoServiceExceptionHandler>> _mockLogger;
    private readonly HvoServiceExceptionHandler _handler;
    private readonly Mock<HttpContext> _mockHttpContext;

    public HvoServiceExceptionHandlerTests()
    {
        _mockProblemDetailsService = new Mock<IProblemDetailsService>();
        _mockLogger = new Mock<ILogger<HvoServiceExceptionHandler>>();
        _handler = new HvoServiceExceptionHandler(_mockProblemDetailsService.Object, _mockLogger.Object);
        _mockHttpContext = new Mock<HttpContext>();
    }

    #region TryHandleAsync Tests

    [TestMethod]
    public async Task TryHandleAsync_WithArgumentException_ShouldSetBadRequestStatus()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");
        var cancellationToken = CancellationToken.None;
        
        _mockProblemDetailsService
            .Setup(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.TryHandleAsync(_mockHttpContext.Object, exception, cancellationToken);

        // Assert
        result.Should().BeTrue();
        
        _mockProblemDetailsService.Verify(x => x.TryWriteAsync(It.Is<ProblemDetailsContext>(
            ctx => ctx.ProblemDetails.Status == StatusCodes.Status400BadRequest &&
                   ctx.ProblemDetails.Title == "An error occurred" &&
                   ctx.ProblemDetails.Type == "ArgumentException" &&
                   ctx.ProblemDetails.Detail == "Invalid argument" &&
                   ctx.Exception == exception &&
                   ctx.HttpContext == _mockHttpContext.Object
        )), Times.Once);
    }

    [TestMethod]
    public async Task TryHandleAsync_WithGenericException_ShouldSetInternalServerErrorStatus()
    {
        // Arrange
        var exception = new InvalidOperationException("Operation failed");
        var cancellationToken = CancellationToken.None;
        
        _mockProblemDetailsService
            .Setup(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.TryHandleAsync(_mockHttpContext.Object, exception, cancellationToken);

        // Assert
        result.Should().BeTrue();
        
        _mockProblemDetailsService.Verify(x => x.TryWriteAsync(It.Is<ProblemDetailsContext>(
            ctx => ctx.ProblemDetails.Status == StatusCodes.Status500InternalServerError &&
                   ctx.ProblemDetails.Title == "An error occurred" &&
                   ctx.ProblemDetails.Type == "InvalidOperationException" &&
                   ctx.ProblemDetails.Detail == "Operation failed"
        )), Times.Once);
    }

    [TestMethod]
    public async Task TryHandleAsync_ShouldLogErrorWithProblemDetails()
    {
        // Arrange
        var exception = new Exception("Test exception");
        var cancellationToken = CancellationToken.None;
        
        _mockProblemDetailsService
            .Setup(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(true);

        // Act
        await _handler.TryHandleAsync(_mockHttpContext.Object, exception, cancellationToken);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Problem Details:")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task TryHandleAsync_WhenProblemDetailsServiceReturnsFalse_ShouldReturnFalse()
    {
        // Arrange
        var exception = new Exception("Test exception");
        var cancellationToken = CancellationToken.None;
        
        _mockProblemDetailsService
            .Setup(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.TryHandleAsync(_mockHttpContext.Object, exception, cancellationToken);

        // Assert
        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task TryHandleAsync_WithCancellationToken_ShouldPassTokenToProblemDetailsService()
    {
        // Arrange
        var exception = new Exception("Test exception");
        var cancellationToken = new CancellationToken(true);
        
        _mockProblemDetailsService
            .Setup(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(true);

        // Act
        await _handler.TryHandleAsync(_mockHttpContext.Object, exception, cancellationToken);

        // Assert
        _mockProblemDetailsService.Verify(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()), Times.Once);
    }

    [TestMethod]
    [DataRow(typeof(ArgumentException), StatusCodes.Status400BadRequest)]
    [DataRow(typeof(ArgumentNullException), StatusCodes.Status400BadRequest)]
    [DataRow(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError)]
    [DataRow(typeof(Exception), StatusCodes.Status500InternalServerError)]
    [DataRow(typeof(NotImplementedException), StatusCodes.Status500InternalServerError)]
    public async Task TryHandleAsync_WithVariousExceptionTypes_ShouldSetCorrectStatusCode(Type exceptionType, int expectedStatusCode)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test message")!;
        var cancellationToken = CancellationToken.None;
        
        _mockProblemDetailsService
            .Setup(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(true);

        // Act
        await _handler.TryHandleAsync(_mockHttpContext.Object, exception, cancellationToken);

        // Assert
        _mockProblemDetailsService.Verify(x => x.TryWriteAsync(It.Is<ProblemDetailsContext>(
            ctx => ctx.ProblemDetails.Status == expectedStatusCode
        )), Times.Once);
    }

    #endregion
}
