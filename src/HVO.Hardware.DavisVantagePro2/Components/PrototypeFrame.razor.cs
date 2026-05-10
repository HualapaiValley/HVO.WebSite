using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.DavisVantagePro2.Components;

public partial class PrototypeFrame
{
    [Parameter, EditorRequired] public string Eyebrow { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    [Parameter] public RenderFragment? HeaderAside { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }
}