using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Simulation;
using HVO.Hardware.Eg4.Telemetry;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Hosting;

public static class Eg4ServiceCollectionExtensions
{
    public static IServiceCollection AddEg4GatewayCore(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<Eg4Options>()
            .Bind(configuration.GetSection(Eg4Options.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Eg4Options>, Eg4OptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEg4OutboxDashboardProvider, UnavailableEg4OutboxDashboardProvider>();
        services.AddSingleton<Eg4GatewayDashboardState>();
        services.AddSingleton<IEg4GatewayDashboardState>(provider => provider.GetRequiredService<Eg4GatewayDashboardState>());
        services.AddSingleton<IEg4GatewayDashboardPublisher>(provider => provider.GetRequiredService<Eg4GatewayDashboardState>());

        if (configuration.GetValue<bool>($"{Eg4Options.SectionName}:SimulationEnabled"))
        {
            services.AddSingleton<Eg4FleetSimulator>();
            services.AddSingleton<IEg4TelemetrySource>(provider => provider.GetRequiredService<Eg4FleetSimulator>());
            services.AddSingleton<ScriptedEg4RegisterTransportFactory>();
            services.AddSingleton<IEg4RegisterTransportFactory>(provider =>
                provider.GetRequiredService<ScriptedEg4RegisterTransportFactory>());
            services.AddSingleton<IEg4PortCoordinator, Eg4PortCoordinator>();
        }
        else
        {
            services.AddSingleton<Eg46500ExHidrawTransportFactory>();
            services.AddSingleton<IEg46500ExInquiryTransportFactory>(provider =>
                provider.GetRequiredService<Eg46500ExHidrawTransportFactory>());
            services.AddSingleton<Eg46500ExTelemetrySource>();
            services.AddSingleton<IEg4DeviceTelemetrySource>(provider => provider.GetRequiredService<Eg46500ExTelemetrySource>());
            services.AddSingleton<Eg4Mppt10048HvSerialTransportFactory>();
            services.AddSingleton<IEg4RegisterTransportFactory>(provider =>
                provider.GetRequiredService<Eg4Mppt10048HvSerialTransportFactory>());
            services.AddSingleton<IEg4PortCoordinator, Eg4PortCoordinator>();
            services.AddSingleton<Eg4Mppt10048HvTelemetrySource>();
            services.AddSingleton<IEg4DeviceTelemetrySource>(provider => provider.GetRequiredService<Eg4Mppt10048HvTelemetrySource>());
            services.AddSingleton<IEg4TelemetrySource, Eg4TelemetrySourceRouter>();
        }

        return services;
    }
}
