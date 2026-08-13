using HVO.Edge.Contracts;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Hosting.Configuration;

public sealed class SecretFileResolver(IOptions<EdgePathOptions> paths)
{
    public string ReadRequired(string reference, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException($"{configurationKey} is required.");
        if (Path.IsPathFullyQualified(reference) || !string.Equals(Path.GetFileName(reference), reference, StringComparison.Ordinal))
            throw new InvalidOperationException($"{configurationKey} must be a file name relative to the secrets directory.");

        var root = Path.GetFullPath(paths.Value.SecretsDirectory);
        var path = Path.GetFullPath(Path.Combine(root, reference));
        if (!EdgePathOptionsValidator.IsWithin(path, root))
            throw new InvalidOperationException($"{configurationKey} resolves outside the secrets directory.");
        if (!File.Exists(path))
            throw new InvalidOperationException($"The secret referenced by {configurationKey} does not exist.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"The secret referenced by {configurationKey} must not be a symbolic link.");

        var value = File.ReadAllText(path).Trim();
        if (!GatewayApiKeyMatcher.IsMatch(value, value))
            throw new InvalidOperationException($"The secret referenced by {configurationKey} is empty or a placeholder.");
        return value;
    }
}
