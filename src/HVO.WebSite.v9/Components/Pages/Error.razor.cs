using Microsoft.AspNetCore.Components;
using System.Diagnostics;

namespace HVO.WebSite.v9.Components.Pages;

/// <summary>
/// Error page component for displaying application errors with request tracking
/// </summary>
public partial class Error
{
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private string? RequestId { get; set; }
    private bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    protected override void OnInitialized() =>
        RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
}
