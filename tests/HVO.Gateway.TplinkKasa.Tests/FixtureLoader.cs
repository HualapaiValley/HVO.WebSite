namespace HVO.Gateway.TplinkKasa.Tests;

internal static class FixtureLoader
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
