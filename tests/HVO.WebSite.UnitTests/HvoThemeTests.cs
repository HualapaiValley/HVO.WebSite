using FluentAssertions;
using HVO.WebSite.Themes.Components.Layout;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HvoThemeTests
{
    [TestMethod]
    public void Create_ReturnsMudTheme()
    {
        var theme = HvoTheme.Create();
        theme.Should().NotBeNull();
    }

    [TestMethod]
    public void Create_PaletteLight_IsNotEmpty()
    {
        var theme = HvoTheme.Create();
        theme.PaletteLight.Primary.ToString().Should().NotBeNullOrEmpty();
        theme.PaletteLight.Secondary.ToString().Should().NotBeNullOrEmpty();
        theme.PaletteLight.Background.ToString().Should().NotBeNullOrEmpty();
        theme.PaletteLight.Surface.ToString().Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Create_PaletteDark_IsNotEmpty()
    {
        var theme = HvoTheme.Create();
        theme.PaletteDark.Primary.ToString().Should().NotBeNullOrEmpty();
        theme.PaletteDark.Secondary.ToString().Should().NotBeNullOrEmpty();
        theme.PaletteDark.Background.ToString().Should().NotBeNullOrEmpty();
        theme.PaletteDark.Surface.ToString().Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Create_CalledTwice_ReturnsNewInstances()
    {
        var a = HvoTheme.Create();
        var b = HvoTheme.Create();
        a.Should().NotBeSameAs(b);
    }
}
