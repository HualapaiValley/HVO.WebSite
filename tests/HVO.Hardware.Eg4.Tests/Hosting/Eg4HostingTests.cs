using FluentAssertions;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Hosting;
using HVO.Hardware.Eg4.Simulation;
using HVO.Hardware.Eg4.Telemetry;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HVO.Hardware.Eg4.Tests.Hosting;

[TestClass]
public sealed class Eg4HostingTests
{
    [TestMethod]
    public async Task TestingSimulation_ResolvesSameSimulatorThroughDiAndPreservesTimeProvider()
    {
        var fakeTime = new FakeTimeProvider();
        var builder = CreateBuilder("Testing", simulationEnabled: true, displayTimeZoneId: "America/New_York");
        builder.Services.AddSingleton<TimeProvider>(fakeTime);
        builder.Services.AddEg4GatewayCore(builder.Configuration, builder.Environment);
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetRequiredService<TimeProvider>().Should().BeSameAs(fakeTime);
        host.Services.GetRequiredService<IEg4TelemetrySource>()
            .Should().BeSameAs(host.Services.GetRequiredService<Eg4FleetSimulator>());
        host.Services.GetRequiredService<HvoDisplayTimeZone>().Label.Should().Be("America/New_York");
        await host.StopAsync();
    }

    [TestMethod]
    public async Task InvalidDisplayTimeZone_FailsStartup()
    {
        var builder = CreateBuilder("Testing", simulationEnabled: true, displayTimeZoneId: "not-a-time-zone");
        builder.Services.AddEg4GatewayCore(builder.Configuration, builder.Environment);
        using var host = builder.Build();

        await FluentActions.Awaiting(() => host.StartAsync())
            .Should().ThrowAsync<OptionsValidationException>();
    }

    [TestMethod]
    public async Task ProductionAndStagingSimulation_FailStartup()
    {
        foreach (var environment in new[] { "Production", "Staging" })
        {
            var builder = CreateBuilder(environment, simulationEnabled: true);
            builder.Services.AddEg4GatewayCore(builder.Configuration, builder.Environment);
            using var host = builder.Build();

            await FluentActions.Awaiting(() => host.StartAsync())
                .Should().ThrowAsync<OptionsValidationException>();
        }
    }

    [TestMethod]
    public async Task ProductionWithoutSimulation_StartsWithoutSimulatorServices()
    {
        var builder = CreateBuilder("Production", simulationEnabled: false);
        builder.Services.AddEg4GatewayCore(builder.Configuration, builder.Environment);
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetService<Eg4FleetSimulator>().Should().BeNull();
        var source = host.Services.GetRequiredService<IEg4TelemetrySource>();
        source.Should().BeOfType<Eg4TelemetrySourceRouter>();
        source.Supports(Eg4DeviceType.Inverter6500Ex).Should().BeTrue();
        source.Supports(Eg4DeviceType.ChargeControllerMppt10048Hv).Should().BeTrue();
        await host.StopAsync();
    }

    private static HostApplicationBuilder CreateBuilder(
        string environment,
        bool simulationEnabled,
        string displayTimeZoneId = "America/Phoenix")
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Eg4:SimulationEnabled"] = simulationEnabled.ToString(),
            ["Eg4:DefaultPollIntervalSeconds"] = "60",
            ["Eg4:DisplayTimeZoneId"] = displayTimeZoneId,
        });
        return builder;
    }
}
