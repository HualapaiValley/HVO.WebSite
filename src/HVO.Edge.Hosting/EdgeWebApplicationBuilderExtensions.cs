using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Hosting.Logging;
using HVO.Edge.Hosting.Telemetry;
using HVO.Edge.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Hosting;

public static class EdgeWebApplicationBuilderExtensions
{
    public static EdgeRuntimeIdentity AddHvoEdgeRuntime(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddHvoEdgeMountedConfiguration();

        var identity = EdgeRuntimeIdentity.Resolve(builder.Configuration, builder.Environment);
        builder.Host.UseHvoGatewayLogging(identity);

        builder.Services.AddOptions<EdgeRuntimeOptions>()
            .Bind(builder.Configuration.GetSection(EdgeRuntimeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddOptions<EdgePathOptions>()
            .Bind(builder.Configuration.GetSection(EdgePathOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EdgePathOptions>, EdgePathOptionsValidator>());

        builder.Services.AddSingleton(identity);
        builder.Services.AddSingleton<EdgeRuntimeState>();
        builder.Services.AddSingleton<SecretFileResolver>();
        builder.Services.AddSingleton(provider =>
        {
            var runtime = provider.GetRequiredService<IOptions<EdgeRuntimeOptions>>().Value;
            var resolver = provider.GetRequiredService<SecretFileResolver>();
            return new EdgeDiagnosticsCredential(resolver.ReadRequired(
                runtime.DiagnosticsApiKeySecret,
                "Edge:Runtime:DiagnosticsApiKeySecret"));
        });
        builder.Services.TryAddSingleton<IEdgeDiagnosticsSnapshotProvider, DefaultEdgeDiagnosticsSnapshotProvider>();
        builder.Services.AddSingleton<EdgeDiagnosticsAuthorizationFilter>();
        builder.Services.AddHostedService<EdgeRuntimeInitializer>();

        builder.Services.AddHvoEdgeTelemetry(identity, builder.Configuration);
        builder.Services.AddHvoEdgeOutbox(builder.Configuration);
        builder.Services.AddHttpClient("HvoEdge")
            .AddStandardResilienceHandler();
        return identity;
    }
}
