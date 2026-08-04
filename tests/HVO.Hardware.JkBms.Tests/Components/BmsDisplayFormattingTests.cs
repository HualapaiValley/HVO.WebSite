using FluentAssertions;
using HVO.Hardware.JkBms.Components.Pages;

namespace HVO.Hardware.JkBms.Tests.Components;

[TestClass]
public sealed class BmsDisplayFormattingTests
{
    [TestMethod]
    public void IntoPackCurrentAmps_PreservesObservedPackFlowSign()
    {
        BmsDisplayFormatting.IntoPackCurrentAmps(12500).Should().Be(12.5);
        BmsDisplayFormatting.IntoPackCurrentAmps(-12500).Should().Be(-12.5);
    }

    [TestMethod]
    public void IntoPackPowerWatts_UsesPackVoltageAndNormalizedCurrent()
    {
        BmsDisplayFormatting.IntoPackPowerWatts(53200, 12500).Should().Be(665);
        BmsDisplayFormatting.IntoPackPowerWatts(53200, -12500).Should().Be(-665);
    }

    [TestMethod]
    public void FlowLabel_DescribesNormalizedPowerDirection()
    {
        BmsDisplayFormatting.FlowLabel(665).Should().Be("Into pack");
        BmsDisplayFormatting.FlowLabel(-665).Should().Be("Out of pack");
        BmsDisplayFormatting.FlowLabel(0).Should().Be("Idle");
    }

    [TestMethod]
    public void SignedLabels_ShowExplicitDirection()
    {
        BmsDisplayFormatting.CurrentLabel(12500).Should().Be("+12.5 A");
        BmsDisplayFormatting.CurrentLabel(-12500).Should().Be("-12.5 A");
        BmsDisplayFormatting.PowerLabel(665).Should().Be("+665 W");
        BmsDisplayFormatting.PowerLabel(-665).Should().Be("-665 W");
    }
}