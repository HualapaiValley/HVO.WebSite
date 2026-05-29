using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaJsonShapeSummarizerTests
{
    [TestMethod]
    public void Summarize_ObjectWithNestedArrays_ReturnsPathsAndKindsWithoutValues()
    {
        using var document = JsonDocument.Parse("""
        {
          "system": {
            "get_sysinfo": {
              "deviceId": "secret",
              "relay_state": 1,
              "children": [
                { "id": "child-1", "state": 1 },
                { "id": "child-2", "state": 0, "next_action": { "type": 0 } }
              ]
            }
          }
        }
        """);

        var shapes = KasaJsonShapeSummarizer.Summarize(document.RootElement);

        shapes.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.deviceId", "string"));
        shapes.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.relay_state", "number"));
        shapes.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.children", "array"));
        shapes.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.children[].id", "string"));
        shapes.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.children[].state", "number"));
        shapes.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.children[].next_action.type", "number"));
        shapes.Select(shape => shape.Path).Should().NotContain("secret");
        shapes.Select(shape => shape.Path).Should().NotContain("child-1");
    }
}
