using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HVO.Edge.Hosting.Configuration;

public static class EdgeConfigurationBuilderExtensions
{
    public const string ConfigurationFileEnvironmentVariable = "HVO_EDGE_CONFIG_FILE";

    public static WebApplicationBuilder AddHvoEdgeMountedConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var path = builder.Configuration[ConfigurationFileEnvironmentVariable]
            ?? builder.Configuration[$"{EdgePathOptions.SectionName}:ConfigurationFile"]
            ?? "/app/config/gateway.json";
        var configRoot = builder.Configuration[$"{EdgePathOptions.SectionName}:ConfigDirectory"]
            ?? "/app/config";
        if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(configRoot)
            || !EdgePathOptionsValidator.IsWithin(path, configRoot))
        {
            throw new InvalidOperationException(
                $"{ConfigurationFileEnvironmentVariable} must select an absolute file under Edge:Paths:ConfigDirectory.");
        }
        var optional = !builder.Environment.IsProduction();

        builder.Configuration.AddJsonFile(path, optional, reloadOnChange: false);
        // Explicit runtime overrides must win over the mounted file.
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{EdgePathOptions.SectionName}:ConfigurationFile"] = path
        });
        return builder;
    }
}
