using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Test class to validate build system
/// </summary>
public class TestClass
{
    [JsonPropertyName("TestProperty")]
    public string TestProperty { get; set; } = string.Empty;
}
