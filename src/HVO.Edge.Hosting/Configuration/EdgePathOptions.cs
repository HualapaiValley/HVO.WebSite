using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Hosting.Configuration;

public sealed class EdgePathOptions
{
    public const string SectionName = "Edge:Paths";

    [Required]
    public string ConfigurationFile { get; set; } = "/app/config/gateway.json";

    [Required]
    public string ConfigDirectory { get; set; } = "/app/config";

    [Required]
    public string DataDirectory { get; set; } = "/app/data";

    [Required]
    public string SecretsDirectory { get; set; } = "/run/secrets";
}

internal sealed class EdgePathOptionsValidator : IValidateOptions<EdgePathOptions>
{
    public ValidateOptionsResult Validate(string? name, EdgePathOptions options)
    {
        var paths = new[]
        {
            ("Edge:Paths:ConfigurationFile", options.ConfigurationFile),
            ("Edge:Paths:ConfigDirectory", options.ConfigDirectory),
            ("Edge:Paths:DataDirectory", options.DataDirectory),
            ("Edge:Paths:SecretsDirectory", options.SecretsDirectory)
        };

        var invalid = paths.FirstOrDefault(item =>
            string.IsNullOrWhiteSpace(item.Item2) || !Path.IsPathFullyQualified(item.Item2));
        if (invalid != default)
            return ValidateOptionsResult.Fail($"{invalid.Item1} must be an absolute path.");

        if (!IsWithin(options.ConfigurationFile, options.ConfigDirectory))
            return ValidateOptionsResult.Fail("Edge:Paths:ConfigurationFile must remain under Edge:Paths:ConfigDirectory.");

        return ValidateOptionsResult.Success;
    }

    internal static bool IsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(fullPath, fullRoot, StringComparison.Ordinal);
    }
}
