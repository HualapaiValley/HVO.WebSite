namespace HVO.Hardware.JkBms.Components.Layout;

public enum ShellFooterIndicator
{
    None,
    Online,
    Offline,
    Warning
}

public sealed record ShellFooterItem(string Text, ShellFooterIndicator Indicator = ShellFooterIndicator.None);

public sealed class ShellLayoutState
{
    private static readonly ShellFooterItem DefaultFooterSlot1 = new("JK BMS");
    private static readonly ShellFooterItem DefaultFooterSlot2 = new("Fleet summary");
    private static readonly ShellFooterItem DefaultFooterSlot3 = new("Waiting for samples");
    private static readonly ShellFooterItem DefaultFooterSlot4 = new("Bank telemetry");
    private static readonly ShellFooterItem DefaultFooterSlot5 = new("API sync");

    public event Action? Changed;

    public bool IsDarkMode { get; private set; } = true;

    public string CurrentSection { get; private set; } = "Overview";

    public string PageTitle { get; private set; } = "JK battery dashboard";

    public string PageSummary { get; private set; } = "Live fleet view for the JK BMS banks with per-bank detail and forwarding health.";

    public ShellFooterItem FooterSlot1 { get; private set; } = DefaultFooterSlot1;

    public ShellFooterItem FooterSlot2 { get; private set; } = DefaultFooterSlot2;

    public ShellFooterItem FooterSlot3 { get; private set; } = DefaultFooterSlot3;

    public ShellFooterItem FooterSlot4 { get; private set; } = DefaultFooterSlot4;

    public ShellFooterItem FooterSlot5 { get; private set; } = DefaultFooterSlot5;

    public void SetTheme(bool isDarkMode)
    {
        if (IsDarkMode == isDarkMode)
            return;

        IsDarkMode = isDarkMode;
        NotifyChanged();
    }

    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        NotifyChanged();
    }

    public void SetPage(string currentSection, string pageTitle, string pageSummary)
    {
        var hasChanged = false;

        if (!string.Equals(CurrentSection, currentSection, StringComparison.Ordinal))
        {
            CurrentSection = currentSection;
            hasChanged = true;
        }

        if (!string.Equals(PageTitle, pageTitle, StringComparison.Ordinal))
        {
            PageTitle = pageTitle;
            hasChanged = true;
        }

        if (!string.Equals(PageSummary, pageSummary, StringComparison.Ordinal))
        {
            PageSummary = pageSummary;
            hasChanged = true;
        }

        if (hasChanged)
            NotifyChanged();
    }

    public void SetFooter(
        ShellFooterItem footerSlot1,
        ShellFooterItem footerSlot2,
        ShellFooterItem footerSlot3,
        ShellFooterItem footerSlot4,
        ShellFooterItem footerSlot5)
    {
        var hasChanged = false;

        if (FooterSlot1 != footerSlot1)
        {
            FooterSlot1 = footerSlot1;
            hasChanged = true;
        }

        if (FooterSlot2 != footerSlot2)
        {
            FooterSlot2 = footerSlot2;
            hasChanged = true;
        }

        if (FooterSlot3 != footerSlot3)
        {
            FooterSlot3 = footerSlot3;
            hasChanged = true;
        }

        if (FooterSlot4 != footerSlot4)
        {
            FooterSlot4 = footerSlot4;
            hasChanged = true;
        }

        if (FooterSlot5 != footerSlot5)
        {
            FooterSlot5 = footerSlot5;
            hasChanged = true;
        }

        if (hasChanged)
            NotifyChanged();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
