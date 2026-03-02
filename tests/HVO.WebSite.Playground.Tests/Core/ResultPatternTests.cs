using FluentAssertions;
using HVO.Core.Results;

namespace HVO.WebSite.Playground.Tests.Core;

/// <summary>
/// Unit tests for the Result pattern implementation
/// Ensures the Result&lt;T&gt; type works correctly in our service layer
/// </summary>
[TestClass]public class ResultPatternTests
{
    #region Success Path Tests

    [TestMethod]
    public void Result_WithSuccessValue_ShouldBeSuccessful()
    {
        // Arrange
        var value = "test-value";

        // Act
        var result = Result<string>.Success(value);

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(value);
        result.Error.Should().BeNull();
    }

    [TestMethod]
    public void Result_WithImplicitSuccess_ShouldBeSuccessful()
    {
        // Arrange & Act
        Result<int> result = 42;

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    #endregion

    #region Failure Path Tests

    [TestMethod]
    public void Result_WithException_ShouldBeFailure()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = Result<string>.Failure(exception);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(exception);
    }

    [TestMethod]
    public void Result_WithImplicitException_ShouldBeFailure()
    {
        // Arrange
        var exception = new ArgumentException("Test error");

        // Act
        Result<string> result = exception;

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().Be(exception);
    }

    [TestMethod]
    public void Result_AccessingValueOnFailure_ShouldThrowOriginalException()
    {
        // Arrange
        var originalException = new InvalidOperationException("Original error");
        var result = Result<string>.Failure(originalException);

        // Act
        Action act = () => { var _ = result.Value; };

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .Which.Should().BeSameAs(originalException);
    }

    #endregion

    #region Match Method Tests

    [TestMethod]
    public void Result_Match_WithSuccess_ShouldCallSuccessFunction()
    {
        // Arrange
        var result = Result<int>.Success(42);
        var successCalled = false;
        var failureCalled = false;

        // Act
        var output = result.Match(
            success: value => { successCalled = true; return $"Success: {value}"; },
            failure: error => { failureCalled = true; return "Failure"; }
        );

        // Assert
        successCalled.Should().BeTrue();
        failureCalled.Should().BeFalse();
        output.Should().Be("Success: 42");
    }

    [TestMethod]
    public void Result_Match_WithFailure_ShouldCallFailureFunction()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        var result = Result<int>.Failure(exception);
        var successCalled = false;
        var failureCalled = false;

        // Act
        var output = result.Match(
            success: value => { successCalled = true; return "Success"; },
            failure: error => { failureCalled = true; return $"Failure: {error?.Message}"; }
        );

        // Assert
        successCalled.Should().BeFalse();
        failureCalled.Should().BeTrue();
        output.Should().Be("Failure: Test error");
    }

    #endregion

    #region Operator Tests

    [TestMethod]
    public void Result_ExplicitCastToValue_WithSuccess_ShouldReturnValue()
    {
        // Arrange
        var result = Result<string>.Success("test");

        // Act
        var value = (string)result;

        // Assert
        value.Should().Be("test");
    }

    [TestMethod]
    public void Result_ExplicitCastToValue_WithFailure_ShouldThrowException()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        var result = Result<string>.Failure(exception);

        // Act
        Action act = () => { var _ = (string)result; };

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .Which.Should().BeSameAs(exception);
    }

    [TestMethod]
    public void Result_ExplicitCastToException_ShouldReturnError()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        var result = Result<string>.Failure(exception);

        // Act
        var error = (Exception?)result;

        // Assert
        error.Should().Be(exception);
    }

    [TestMethod]
    public void Result_ExplicitCastToException_WithSuccess_ShouldReturnNull()
    {
        // Arrange
        var result = Result<string>.Success("test");

        // Act
        var error = (Exception?)result;

        // Assert
        error.Should().BeNull();
    }

    #endregion

    #region Real-World Usage Tests

    [TestMethod]
    public void Result_InServiceLayerPattern_ShouldWorkCorrectly()
    {
        // Arrange - Simulate service method that might fail
        Result<string> GetWeatherData(bool shouldSucceed)
        {
            if (shouldSucceed)
                return "Weather data retrieved successfully";
            else
                return new InvalidOperationException("Weather service unavailable");
        }

        // Act & Assert - Success case
        var successResult = GetWeatherData(true);
        successResult.IsSuccessful.Should().BeTrue();
        successResult.Value.Should().Be("Weather data retrieved successfully");

        // Act & Assert - Failure case
        var failureResult = GetWeatherData(false);
        failureResult.IsSuccessful.Should().BeFalse();
        failureResult.Error.Should().BeOfType<InvalidOperationException>();
        failureResult.Error!.Message.Should().Be("Weather service unavailable");
    }

    [TestMethod]
    public void Result_InControllerPattern_ShouldEnableCleanErrorHandling()
    {
        // Arrange - Simulate controller using Result pattern
        string HandleRequest(Result<string> serviceResult)
        {
            return serviceResult.Match(
                success: data => $"OK: {data}",
                failure: error => error switch
                {
                    InvalidOperationException => "NotFound: Service unavailable",
                    ArgumentException => "BadRequest: Invalid input",
                    _ => "InternalServerError: Unexpected error"
                }
            );
        }

        // Act & Assert - Different error types
        var invalidOpResult = Result<string>.Failure(new InvalidOperationException("Service down"));
        HandleRequest(invalidOpResult).Should().Be("NotFound: Service unavailable");

        var argResult = Result<string>.Failure(new ArgumentException("Bad input"));
        HandleRequest(argResult).Should().Be("BadRequest: Invalid input");

        var genericResult = Result<string>.Failure(new Exception("Unknown error"));
        HandleRequest(genericResult).Should().Be("InternalServerError: Unexpected error");

        var successResult = Result<string>.Success("All good");
        HandleRequest(successResult).Should().Be("OK: All good");
    }

    #endregion
}
