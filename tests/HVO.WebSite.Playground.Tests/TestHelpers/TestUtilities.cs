using Microsoft.Extensions.Logging;
using Moq;

namespace HVO.WebSite.Playground.Tests.TestHelpers;

/// <summary>
/// Provides common test utilities and mock configurations
/// </summary>
public static class TestUtilities
{
    /// <summary>
    /// Creates a mock ILogger that can be used in tests
    /// </summary>
    /// <typeparam name="T">The type the logger is for</typeparam>
    /// <returns>A configured mock logger</returns>
    public static Mock<ILogger<T>> CreateMockLogger<T>()
    {
        var mockLogger = new Mock<ILogger<T>>();
        
        // Setup the logger to handle all log levels
        mockLogger.Setup(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Verifiable();

        return mockLogger;
    }

    /// <summary>
    /// Verifies that a logger was called with a specific log level
    /// </summary>
    /// <typeparam name="T">The type the logger is for</typeparam>
    /// <param name="mockLogger">The mock logger to verify</param>
    /// <param name="logLevel">The expected log level</param>
    /// <param name="times">The expected number of times the log was called</param>
    public static void VerifyLogLevel<T>(Mock<ILogger<T>> mockLogger, LogLevel logLevel, Times times)
    {
        mockLogger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    /// <summary>
    /// Verifies that a logger was called with a specific message containing certain text
    /// </summary>
    /// <typeparam name="T">The type the logger is for</typeparam>
    /// <param name="mockLogger">The mock logger to verify</param>
    /// <param name="logLevel">The expected log level</param>
    /// <param name="expectedMessagePart">Part of the expected log message</param>
    public static void VerifyLogMessage<T>(Mock<ILogger<T>> mockLogger, LogLevel logLevel, string expectedMessagePart)
    {
        mockLogger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessagePart)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
