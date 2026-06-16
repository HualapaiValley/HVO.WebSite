namespace HVO.Hardware.DavisVantagePro2.Outbox;

internal static class OutboxDatabasePath
{
    public static string Resolve(string? configuredPath, string? localApplicationDataPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        string defaultOutboxRoot = localApplicationDataPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(defaultOutboxRoot))
        {
            defaultOutboxRoot = Path.Combine(Path.GetTempPath(), "hvo-davis");
        }

        return Path.Combine(defaultOutboxRoot, "HVO", "DavisVantagePro2", "outbox.db");
    }
}
