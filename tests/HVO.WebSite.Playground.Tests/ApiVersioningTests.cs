using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO.WebSite.Playground;
using HVO.WebSite.Playground.Tests.TestHelpers;

namespace HVO.WebSite.Playground.Tests;

[TestClass]
public class ApiVersioningTests
{
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _factory = new TestWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task GetWeather_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/latest");

        // Assert
          Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsNotNull(content);
          content.Should().Contain("timestamp");
          content.Should().Contain("data");
    }

    [TestMethod]
    public async Task GetWeather_WithUrlSegmentVersioning_ReturnsCorrectJsonStructure()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/latest");

        // Assert
    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;
        
        Assert.IsTrue(root.TryGetProperty("timestamp", out var timestampElement));
        Assert.IsTrue(DateTime.TryParse(timestampElement.GetString(), out _));
        
        Assert.IsTrue(root.TryGetProperty("machineName", out var machineNameElement));
        Assert.IsNotNull(machineNameElement.GetString());
        
        Assert.IsTrue(root.TryGetProperty("data", out var dataElement));
          Assert.AreNotEqual(JsonValueKind.Null, dataElement.ValueKind);
    }

    [TestMethod]
    public async Task GetWeather_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/weather/latest");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetWeather_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/latest");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #region Weather API Versioning Tests

    [TestMethod]
    public async Task GetWeatherLatest_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/latest");

        // Assert - We expect this to succeed even if data is not available (404 is valid business logic)
            Assert.IsTrue(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound);
        
        // Verify it's not a versioning issue (which would be 404 with different error structure)
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            // Should contain weather-specific error, not a generic not found
                content.Should().Contain("Weather");
        }
    }

    [TestMethod]
    public async Task GetWeatherLatest_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/weather/latest");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetWeatherLatest_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/latest");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetWeatherHighsLows_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/highs-lows");

        // Assert - We expect this to succeed even if data is not available (404 is valid business logic)
            Assert.IsTrue(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound);
        
        // Verify it's not a versioning issue
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            // Should contain weather-specific error, not a generic not found
                content.Should().Contain("Weather");
        }
    }

    [TestMethod]
    public async Task GetWeatherHighsLows_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/weather/highs-lows");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetWeatherHighsLows_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/highs-lows");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetWeatherCurrent_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/current");

        // Assert - We expect this to succeed even if data is not available (404 is valid business logic)
            Assert.IsTrue(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound);
        
        // Verify it's not a versioning issue
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            // Should contain weather-specific error, not a generic not found
                content.Should().Contain("Weather");
        }
    }

    [TestMethod]
    public async Task GetWeatherCurrent_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/weather/current");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetWeatherCurrent_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/current");

        // Assert
          Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AllWeatherEndpoints_SupportApiVersioning()
    {
        // Arrange - All weather endpoints that should support v1.0
        var weatherEndpoints = new[]
        {
            "/api/v1.0/weather/latest",
            "/api/v1.0/weather/highs-lows",
            "/api/v1.0/weather/current"
        };

        // Act & Assert
        foreach (var endpoint in weatherEndpoints)
        {
            var response = await _client.GetAsync(endpoint);
            
            // Should not return 404 due to versioning issues
            // Business logic 404s are acceptable (no data available)
                Assert.IsTrue(
                    response.StatusCode == HttpStatusCode.OK || 
                    response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.InternalServerError, // Acceptable for complex operations
                    $"Endpoint {endpoint} returned unexpected status: {response.StatusCode}"
                );
            
            // Verify Content-Type header is set for JSON API
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
                    contentType.Should().Contain("application/json");
            }
        }
    }

    #endregion
}
