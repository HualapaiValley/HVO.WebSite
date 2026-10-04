using Microsoft.AspNetCore.Components.Web;

namespace HVO.WebSite.v9.Components.Pages;

public partial class Home
{
    private ErrorBoundary? _dashboardBoundary;
    private void RecoverDashboard() => _dashboardBoundary?.Recover();
}
