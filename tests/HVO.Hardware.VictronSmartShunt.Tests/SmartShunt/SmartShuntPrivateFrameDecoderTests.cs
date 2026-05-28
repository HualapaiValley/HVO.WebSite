using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt;

[TestClass]
public sealed class SmartShuntPrivateFrameDecoderTests
{
    [TestMethod]
    public void Observe_ResynchronizesPastLeadingWrapperBytes_AndDecodesTrailingHistoryFrames()
    {
        var decoder = new SmartShuntPrivateFrameDecoder();

        decoder.Observe(Convert.FromHexString("09446802000008031903044401000000080319031d4100080319031e420000090319103003090319eefc010803190328420000"));

        var info = decoder.Build();

        info.Should().NotBeNull();
        info!.Overlay.Should().NotBeNull();
        info.Overlay!.FullDischarges.Should().Be(1);
    }

    [TestMethod]
    public void Observe_DecodesAlarmThresholdHistoryRegisters()
    {
        var decoder = new SmartShuntPrivateFrameDecoder();

        decoder.Observe(Convert.FromHexString("080319032044f81100000803190321445c120000080319032844c8000000080319032944fa000000"));

        var info = decoder.Build();

        info.Should().NotBeNull();
        info!.Overlay.Should().NotBeNull();
        info.Overlay!.AlarmLowVoltageSetV.Should().Be(46.00);
        info.Overlay.AlarmLowVoltageClearV.Should().Be(47.00);
        info.Overlay.AlarmLowSocSetPercent.Should().Be(20.0);
        info.Overlay.AlarmLowSocClearPercent.Should().Be(25.0);
    }

    [TestMethod]
    public void Observe_DecodesStreamingCounterAndCoarseChargeStatus()
    {
        var decoder = new SmartShuntPrivateFrameDecoder();

        decoder.Observe(Convert.FromHexString("080319ec5a4439300000090319ec875e"));

        var info = decoder.Build();

        info.Should().NotBeNull();
        info!.Overlay.Should().NotBeNull();
        info.Overlay!.StreamingCounter.Should().Be(12345);
        info.Overlay.ChargeStatusCoarsePercent.Should().Be(94);
    }

    [TestMethod]
    public void Observe_DecodesCoarseCurrentField()
    {
        var decoder = new SmartShuntPrivateFrameDecoder();

        decoder.Observe(Convert.FromHexString("080319ed8f4285ff"));

        var info = decoder.Build();

        info.Should().NotBeNull();
        info!.Overlay.Should().NotBeNull();
        info.Overlay!.CurrentCoarseA.Should().Be(-12.3);
    }

    [TestMethod]
    public void Observe_PreservesRawProductMetadataField()
    {
        var decoder = new SmartShuntPrivateFrameDecoder();

        decoder.Observe(Convert.FromHexString("0803190109483aaa47ca2438e600"));

        var info = decoder.Build();

        info.Should().NotBeNull();
        info!.ProductMetadataRaw.Should().Be("3aaa47ca2438e600");
    }

    [TestMethod]
    public void Observe_PreservesRawProductFamilyField()
    {
        var decoder = new SmartShuntPrivateFrameDecoder();

        decoder.Observe(Convert.FromHexString("0803190100440000fefe"));

        var info = decoder.Build();

        info.Should().NotBeNull();
        info!.ProductFamilyRaw.Should().Be("0000fefe");
        info.ProductId.Should().Be("0000fefe");
    }
}
