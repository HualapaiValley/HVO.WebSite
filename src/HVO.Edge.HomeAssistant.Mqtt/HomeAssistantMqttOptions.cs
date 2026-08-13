using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt;

public sealed class HomeAssistantMqttOptions
{
    public const string SectionName = "HomeAssistant:Mqtt";

    public bool Enabled { get; set; }

    public string? Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; } = 1883;

    public string? UsernameSecret { get; set; }

    public string? PasswordSecret { get; set; }

    [Required]
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    [Required]
    public string TopicPrefix { get; set; } = "hvo";

    [Range(1, 300)]
    public int InitialReconnectDelaySeconds { get; set; } = 1;

    [Range(1, 300)]
    public int MaxReconnectDelaySeconds { get; set; } = 30;
}

internal sealed class HomeAssistantMqttOptionsValidator : IValidateOptions<HomeAssistantMqttOptions>
{
    public ValidateOptionsResult Validate(string? name, HomeAssistantMqttOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Host))
            failures.Add("HomeAssistant:Mqtt:Host is required when MQTT is enabled.");
        if (string.IsNullOrWhiteSpace(options.UsernameSecret))
            failures.Add("HomeAssistant:Mqtt:UsernameSecret is required when MQTT is enabled.");
        if (string.IsNullOrWhiteSpace(options.PasswordSecret))
            failures.Add("HomeAssistant:Mqtt:PasswordSecret is required when MQTT is enabled.");
        if (!IsTopicPrefix(options.DiscoveryPrefix))
            failures.Add("HomeAssistant:Mqtt:DiscoveryPrefix must be a non-empty MQTT topic prefix without wildcards.");
        if (!IsTopicPrefix(options.TopicPrefix))
            failures.Add("HomeAssistant:Mqtt:TopicPrefix must be a non-empty MQTT topic prefix without wildcards.");
        if (options.MaxReconnectDelaySeconds < options.InitialReconnectDelaySeconds)
            failures.Add("HomeAssistant:Mqtt:MaxReconnectDelaySeconds must be at least InitialReconnectDelaySeconds.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsTopicPrefix(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains('+', StringComparison.Ordinal)
        && !value.Contains('#', StringComparison.Ordinal)
        && value.Split('/', StringSplitOptions.None).All(segment => segment.Length > 0);
}
