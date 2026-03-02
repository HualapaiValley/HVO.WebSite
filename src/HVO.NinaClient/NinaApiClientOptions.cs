using System.ComponentModel.DataAnnotations;

namespace HVO.NinaClient;

/// <summary>
/// Configuration options for NINA API client with validation
/// </summary>
public class NinaApiClientOptions
{
    /// <summary>
    /// Base URL for NINA API (e.g., "http://localhost:1888")
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:1888";

    /// <summary>
    /// API key for authentication (if required)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 300; // Increased to 5 minutes for long astronomy operations

    /// <summary>
    /// Maximum number of retry attempts for failed requests
    /// </summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds
    /// </summary>
    [Range(100, 30000)]
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Enable circuit breaker pattern for fault tolerance
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Circuit breaker failure threshold (number of consecutive failures)
    /// </summary>
    [Range(1, 20)]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Circuit breaker timeout in seconds before attempting to close
    /// </summary>
    [Range(10, 300)]
    public int CircuitBreakerTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Validates the configuration options
    /// </summary>
    /// <returns>Validation result</returns>
    public ValidationResult Validate()
    {
        var context = new ValidationContext(this);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(this, context, results, true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            return new ValidationResult($"Configuration validation failed: {errors}");
        }

        // Additional custom validation
        if (!Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute))
        {
            return new ValidationResult($"BaseUrl '{BaseUrl}' is not a valid absolute URI");
        }

        var uri = new Uri(BaseUrl);
        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            return new ValidationResult($"BaseUrl scheme must be http or https, got: {uri.Scheme}");
        }

        return ValidationResult.Success!;
    }

    /// <summary>
    /// Validates and throws if configuration is invalid
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    public void ValidateAndThrow()
    {
        var result = Validate();
        if (result != ValidationResult.Success)
        {
            throw new ArgumentException(result.ErrorMessage, nameof(NinaApiClientOptions));
        }
    }
}
