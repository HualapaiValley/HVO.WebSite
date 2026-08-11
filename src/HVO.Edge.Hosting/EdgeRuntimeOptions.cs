using System.ComponentModel.DataAnnotations;
using HVO.Edge.Contracts;

namespace HVO.Edge.Hosting;

public sealed class EdgeRuntimeOptions
{
    public const string SectionName = "Edge:Runtime";

    [Required]
    public string ServiceName { get; set; } = string.Empty;

    [Required]
    public string GatewayId { get; set; } = string.Empty;

    [Required]
    public string GatewayType { get; set; } = string.Empty;

    public GatewayDomain Domain { get; set; } = GatewayDomain.Unknown;

    public string? SourceId { get; set; }

    public string? SiteId { get; set; }

    public string? DeviceId { get; set; }

    public string? DisplayName { get; set; }

    public string? ServiceInstanceId { get; set; }

    [Required]
    public string DiagnosticsApiKeySecret { get; set; } = "diagnostics-api-key";
}
