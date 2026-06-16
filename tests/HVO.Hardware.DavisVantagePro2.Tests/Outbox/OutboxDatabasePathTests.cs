using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Outbox;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public sealed class OutboxDatabasePathTests
{
    [TestMethod]
    public void Resolve_ConfiguredPath_ReturnsConfiguredPath()
    {
        const string configuredPath = "/app/data/outbox.db";

        string result = OutboxDatabasePath.Resolve(configuredPath, "/local/appdata");

        result.Should().Be(configuredPath);
    }

    [TestMethod]
    public void Resolve_BlankConfiguredPath_UsesLocalApplicationData()
    {
        string result = OutboxDatabasePath.Resolve("", "/local/appdata");

        result.Should().Be(Path.Combine("/local/appdata", "HVO", "DavisVantagePro2", "outbox.db"));
    }

    [TestMethod]
    public void Resolve_BlankConfiguredPathAndNoLocalApplicationData_UsesTempPath()
    {
        string result = OutboxDatabasePath.Resolve(" ", "");

        result.Should().Be(Path.Combine(Path.GetTempPath(), "hvo-davis", "HVO", "DavisVantagePro2", "outbox.db"));
    }
}
