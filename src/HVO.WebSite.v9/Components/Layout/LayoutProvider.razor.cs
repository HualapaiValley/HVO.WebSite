using Microsoft.AspNetCore.Components;

namespace HVO.WebSite.v9.Components.Layout;

public partial class LayoutProvider
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    private bool _isDarkMode = true;
    private bool _isAdmin;

    // Router updates Body on navigation. No long-lived LocationChanged subscription is needed.
    protected override void OnParametersSet() => UpdateLayout();

    private void UpdateLayout()
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].TrimStart('/');
        _isAdmin = path.Equals("admin", StringComparison.OrdinalIgnoreCase) || path.StartsWith("admin/", StringComparison.OrdinalIgnoreCase);
    }
}
