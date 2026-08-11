namespace HVO.Edge.Hosting.Diagnostics;

public sealed class EdgeDiagnosticsCredential
{
    internal EdgeDiagnosticsCredential(string apiKey) => ApiKey = apiKey;

    internal string ApiKey { get; }
}
