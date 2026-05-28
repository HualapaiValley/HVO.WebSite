using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Station;

namespace HVO.Hardware.DavisVantagePro2.Tests.Station;

[TestClass]
public class DisplayUnitConverterTests
{
    [TestMethod]
    public void Temperature_CelsiusUnits_ConvertsFahrenheitToCelsius()
    {
        DisplayUnitConverter.Temperature(68.0, "°C").Should().BeApproximately(20.0, 0.0001);
        DisplayUnitConverter.Temperature(68.0, "°C×10").Should().BeApproximately(20.0, 0.0001);
    }

    [TestMethod]
    public void Temperature_FahrenheitUnits_PreservesFahrenheit()
    {
        DisplayUnitConverter.Temperature(68.0, "°F").Should().Be(68.0);
        DisplayUnitConverter.Temperature(68.0, "°F×10").Should().Be(68.0);
    }

    [TestMethod]
    public void Pressure_ConvertsInHgToConfiguredUnits()
    {
        DisplayUnitConverter.Pressure(1.0, "mmHg").Should().BeApproximately(25.4, 0.0001);
        DisplayUnitConverter.Pressure(1.0, "hPa").Should().BeApproximately(33.8638866667, 0.0001);
        DisplayUnitConverter.Pressure(1.0, "mbar").Should().BeApproximately(33.8638866667, 0.0001);
        DisplayUnitConverter.Pressure(1.0, "inHg").Should().Be(1.0);
    }

    [TestMethod]
    public void Rain_MmUnits_ConvertsInchesToMillimeters()
    {
        DisplayUnitConverter.Rain(1.0, "mm").Should().BeApproximately(25.4, 0.0001);
        DisplayUnitConverter.Rain(1.0, "inch").Should().Be(1.0);
    }

    [TestMethod]
    public void WindSpeed_ConvertsMphToConfiguredUnits()
    {
        DisplayUnitConverter.WindSpeed(10.0, "m/s").Should().BeApproximately(4.4704, 0.0001);
        DisplayUnitConverter.WindSpeed(10.0, "km/h").Should().BeApproximately(16.09344, 0.0001);
        DisplayUnitConverter.WindSpeed(10.0, "knots").Should().BeApproximately(8.689762419, 0.0001);
        DisplayUnitConverter.WindSpeed(10.0, "mph").Should().Be(10.0);
    }
}
