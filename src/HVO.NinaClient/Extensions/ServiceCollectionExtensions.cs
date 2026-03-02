using HVO.NinaClient.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace HVO.NinaClient.Extensions;

/// <summary>
/// Extension methods for registering NINA client services with enhanced configuration validation
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add NINA WebSocket client services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration to bind options from</param>
    /// <param name="configurationSection">Configuration section name (default: "NinaWebSocket")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaWebSocketClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSection = "NinaWebSocket")
    {
        // Configure options with validation
        services.Configure<NinaWebSocketOptions>(configuration.GetSection(configurationSection));
        
        // Add validation for options
        services.AddSingleton<IValidateOptions<NinaWebSocketOptions>, ValidateNinaWebSocketOptions>();

        // Register services
        services.TryAddSingleton<INinaWebSocketClient, NinaWebSocketClient>();

        return services;
    }

    /// <summary>
    /// Add NINA WebSocket client services with explicit options
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">WebSocket client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaWebSocketClient(
        this IServiceCollection services,
        NinaWebSocketOptions options)
    {
        // Validate options immediately
        options.ValidateAndThrow();

        // Register the options instance directly
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        // Register services
        services.TryAddSingleton<INinaWebSocketClient, NinaWebSocketClient>();

        return services;
    }

    /// <summary>
    /// Add NINA WebSocket client services with configuration action
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaWebSocketClient(
        this IServiceCollection services,
        Action<NinaWebSocketOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Add validation for options
        services.AddSingleton<IValidateOptions<NinaWebSocketOptions>, ValidateNinaWebSocketOptions>();

        // Register services
        services.TryAddSingleton<INinaWebSocketClient, NinaWebSocketClient>();

        return services;
    }

    /// <summary>
    /// Add NINA API client services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing NINA client settings</param>
    /// <param name="configurationSectionName">Name of configuration section (default: "NinaApiClient")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaApiClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName = "NinaApiClient")
    {
        // Configure options with validation
        services.Configure<NinaApiClientOptions>(
            configuration.GetSection(configurationSectionName));
            
        // Add validation for options
        services.AddSingleton<IValidateOptions<NinaApiClientOptions>, ValidateNinaApiClientOptions>();

        // Register HTTP client - let NinaApiClient configure itself to avoid conflicts
        services.AddHttpClient<INinaApiClient, NinaApiClient>();

        return services;
    }

    /// <summary>
    /// Add NINA API client services with explicit configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure NINA client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaApiClient(
        this IServiceCollection services,
        Action<NinaApiClientOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);
            
        // Add validation for options
        services.AddSingleton<IValidateOptions<NinaApiClientOptions>, ValidateNinaApiClientOptions>();

        // Register HTTP client - let NinaApiClient configure itself to avoid conflicts
        services.AddHttpClient<INinaApiClient, NinaApiClient>();

        return services;
    }

    /// <summary>
    /// Add NINA API client services with explicit options
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">API client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaApiClient(
        this IServiceCollection services,
        NinaApiClientOptions options)
    {
        // Validate options immediately
        options.ValidateAndThrow();

        // Register the options instance directly
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        // Register HTTP client - let NinaApiClient configure itself to avoid conflicts
        services.AddHttpClient<INinaApiClient, NinaApiClient>();

        return services;
    }

    /// <summary>
    /// Add all NINA client services (HTTP API and WebSocket) with enhanced validation
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration to bind options from</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add HTTP API client
        services.AddNinaApiClient(configuration);

        // Add WebSocket client
        services.AddNinaWebSocketClient(configuration);

        return services;
    }

    /// <summary>
    /// Add all NINA client services with explicit configuration actions
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureApiOptions">Action to configure API client options</param>
    /// <param name="configureWebSocketOptions">Action to configure WebSocket client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaClient(
        this IServiceCollection services,
        Action<NinaApiClientOptions> configureApiOptions,
        Action<NinaWebSocketOptions> configureWebSocketOptions)
    {
        // Add HTTP API client
        services.AddNinaApiClient(configureApiOptions);

        // Add WebSocket client
        services.AddNinaWebSocketClient(configureWebSocketOptions);

        return services;
    }
}

/// <summary>
/// Options validator for NinaApiClientOptions
/// </summary>
internal class ValidateNinaApiClientOptions : IValidateOptions<NinaApiClientOptions>
{
    private readonly ILogger<ValidateNinaApiClientOptions> _logger;

    public ValidateNinaApiClientOptions(ILogger<ValidateNinaApiClientOptions> logger)
    {
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, NinaApiClientOptions options)
    {
        try
        {
            var validationResult = options.Validate();
            if (validationResult == System.ComponentModel.DataAnnotations.ValidationResult.Success)
            {
                _logger.LogDebug("NINA API client configuration validation passed");
                return ValidateOptionsResult.Success;
            }

            var errorMessage = validationResult.ErrorMessage ?? "Unknown validation error";
            _logger.LogError("NINA API client configuration validation failed: {Error}", errorMessage);
            return ValidateOptionsResult.Fail(errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during NINA API client configuration validation");
            return ValidateOptionsResult.Fail($"Configuration validation exception: {ex.Message}");
        }
    }
}

/// <summary>
/// Options validator for NinaWebSocketOptions
/// </summary>
internal class ValidateNinaWebSocketOptions : IValidateOptions<NinaWebSocketOptions>
{
    private readonly ILogger<ValidateNinaWebSocketOptions> _logger;

    public ValidateNinaWebSocketOptions(ILogger<ValidateNinaWebSocketOptions> logger)
    {
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, NinaWebSocketOptions options)
    {
        try
        {
            var validationResult = options.Validate();
            if (validationResult == System.ComponentModel.DataAnnotations.ValidationResult.Success)
            {
                _logger.LogDebug("NINA WebSocket client configuration validation passed");
                return ValidateOptionsResult.Success;
            }

            var errorMessage = validationResult.ErrorMessage ?? "Unknown validation error";
            _logger.LogError("NINA WebSocket client configuration validation failed: {Error}", errorMessage);
            return ValidateOptionsResult.Fail(errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during NINA WebSocket client configuration validation");
            return ValidateOptionsResult.Fail($"Configuration validation exception: {ex.Message}");
        }
    }
}
