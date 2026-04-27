using FluentAssertions;
using HVO.Core.Results;
using HVO.DataModels.Models;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;

namespace HVO.WebSite.ApiTests;

[TestClass]
public sealed class WeatherApiEndpointsTests
{
    [TestMethod]
    public async Task LatestEndpoint_ReturnsExpectedPayload()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/weather/latest");

        response.IsSuccessStatusCode.Should().BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<LatestWeatherResponse>();
        payload.Should().NotBeNull();
        payload!.MachineName.Should().Be("api-test-host");
        payload.Data.Should().NotBeNull();
    }

    private sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HualapaiValleyObservatory"] = "Server=localhost;Database=HvoTest;User Id=sa;Password=Password!123;TrustServerCertificate=True;"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWeatherService>();
                services.AddScoped<IWeatherService, FakeWeatherService>();
            });
        }
    }

    private sealed class FakeWeatherService : IWeatherService
    {
        public Task<Result<LatestWeatherResponse>> GetLatestWeatherRecordAsync()
        {
            var latestRecord = new DavisVantageProConsoleRecordsNew
            {
                Id = 1,
                RecordDateTime = DateTimeOffset.UtcNow,
                OutsideTemperature = 72.5m,
                OutsideHumidity = 27,
                WindSpeed = 8,
                WindDirection = 180
            };

            var response = new LatestWeatherResponse
            {
                Timestamp = DateTime.UtcNow,
                MachineName = "api-test-host",
                Data = latestRecord
            };

            return Task.FromResult(Result<LatestWeatherResponse>.Success(response));
        }

        public Task<Result<WeatherHighsLowsResponse>> GetWeatherHighsLowsAsync(DateTimeOffset? startDate, DateTimeOffset? endDate)
            => Task.FromResult<Result<WeatherHighsLowsResponse>>(new InvalidOperationException("Not configured in test"));

        public Task<Result<CurrentWeatherResponse>> GetCurrentWeatherConditionsAsync()
            => Task.FromResult<Result<CurrentWeatherResponse>>(new InvalidOperationException("Not configured in test"));
    }
}
