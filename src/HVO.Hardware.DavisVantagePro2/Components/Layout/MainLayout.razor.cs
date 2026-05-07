using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.DavisVantagePro2.Components.Layout;

public partial class MainLayout : LayoutComponentBase
{
    private bool _navOpen;

    public void ToggleDrawer()
    {
        _navOpen = !_navOpen;
        _ = InvokeAsync(StateHasChanged);
    }

    public void CloseDrawer()
    {
        _navOpen = false;
        _ = InvokeAsync(StateHasChanged);
    }
}