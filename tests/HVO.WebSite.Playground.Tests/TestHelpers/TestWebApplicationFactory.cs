using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HVO.DataModels.Data;
using HVO.WebSite.Playground.Services;
using HVO.WebSite.Playground.Models;
using HVO.DataModels.Models;
using HVO.DataModels.RawModels;
using Moq;
using HVO.Core.Results;

namespace HVO.WebSite.Playground.Tests.TestHelpers;

/// <summary>
/// Custom WebApplicationFactory for integration testing
/// Replaces the SQL Server database with an in-memory database for testing
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove ALL Entity Framework related services - more comprehensive approach
            var efDescriptors = services.Where(d => 
                d.ServiceType == typeof(DbContextOptions<HvoDbContext>) ||
                d.ServiceType == typeof(HvoDbContext) ||
                d.ServiceType == typeof(DbContextOptions) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>)) ||
                (d.ImplementationType?.FullName?.Contains("EntityFramework") == true) ||
                (d.ImplementationType?.FullName?.Contains("SqlServer") == true) ||
                (d.ServiceType.FullName?.Contains("EntityFramework") == true) ||
                (d.ServiceType.FullName?.Contains("SqlServer") == true))
                .ToList();

            foreach (var descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            // Also remove any DbContext-related services that might have been missed
            var dbContextDescriptors = services.Where(d => 
                d.ServiceType.Name.Contains("DbContext") ||
                (d.ImplementationType?.Name.Contains("DbContext") == true))
                .ToList();

            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            // Remove the real WeatherService to replace with mock
            var weatherServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IWeatherService));
            if (weatherServiceDescriptor != null)
            {
                services.Remove(weatherServiceDescriptor);
            }

            // Add in-memory database for testing with unique database name
            services.AddDbContext<HvoDbContext>(options =>
            {
                options.UseInMemoryDatabase($"TestDb-{Guid.NewGuid()}");
                // Ensure we're not using any SQL Server providers
                options.EnableSensitiveDataLogging();
            });

            // Add mocked WeatherService that returns predictable responses
            var mockWeatherService = new Mock<IWeatherService>();
            
            // Mock successful responses for API versioning tests
            mockWeatherService.Setup(x => x.GetLatestWeatherRecordAsync())
                .ReturnsAsync(Result<LatestWeatherResponse>.Success(new LatestWeatherResponse
                {
                    Timestamp = DateTime.UtcNow,
                    MachineName = "TestMachine",
                    Data = new DavisVantageProConsoleRecordsNew
                    {
                        Id = 1,
                        RecordDateTime = DateTimeOffset.UtcNow,
                        OutsideTemperature = 75.0m,
                        OutsideHumidity = 65,
                        Barometer = 30.15m
                    }
                }));

            mockWeatherService.Setup(x => x.GetWeatherHighsLowsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                .ReturnsAsync(Result<WeatherHighsLowsResponse>.Success(new WeatherHighsLowsResponse
                {
                    Timestamp = DateTime.UtcNow,
                    MachineName = "TestMachine",
                    DateRange = new DateRangeInfo
                    {
                        Start = DateTimeOffset.UtcNow.Date,
                        End = DateTimeOffset.UtcNow.Date.AddDays(1)
                    },
                    Data = new WeatherRecordHighLowSummary
                    {
                        StartRecordDateTime = DateTimeOffset.UtcNow.Date,
                        EndRecordDateTime = DateTimeOffset.UtcNow.Date.AddDays(1),
                        OutsideTemperatureHigh = 80.0m,
                        OutsideTemperatureLow = 60.0m,
                        OutsideHumidityHigh = 75,
                        OutsideHumidityLow = 45
                    }
                }));

            mockWeatherService.Setup(x => x.GetCurrentWeatherConditionsAsync())
                .ReturnsAsync(Result<CurrentWeatherResponse>.Success(new CurrentWeatherResponse
                {
                    Timestamp = DateTime.UtcNow,
                    MachineName = "TestMachine",
                    Current = new CurrentWeatherData(),
                    TodaysExtremes = new TodaysExtremesData()
                }));

            services.AddSingleton(mockWeatherService.Object);
        });
        
        builder.UseEnvironment("Testing");
    }
}
