using FluentAssertions;
using HVO.Hardware.JkBms.History;

namespace HVO.Hardware.JkBms.Tests.History;

[TestClass]
public sealed class BmsHistoryCalculationsTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 3, 2, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void CalculateToday_IntegratesChargeAndDischargeEnergyAndAmpHours()
    {
        var points = new[]
        {
            Point("bank-a", TimeSpan.Zero, 50, 1000, 10),
            Point("bank-a", TimeSpan.FromMinutes(5), 55, 1000, 10),
            Point("bank-a", TimeSpan.FromMinutes(10), 54, -500, -5),
        };

        var summary = BmsHistoryCalculations.CalculateToday(points, NowUtc);

        summary.ChargeEnergyKwh.Should().BeApproximately(1d / 12d, 0.001);
        summary.DischargeEnergyKwh.Should().BeApproximately(0.5d / 12d, 0.001);
        summary.ChargeAmpHours.Should().BeApproximately(10d / 12d, 0.001);
        summary.DischargeAmpHours.Should().BeApproximately(5d / 12d, 0.001);
    }

    [TestMethod]
    public void CalculateToday_ProvidesChargeRateAndTimeToFull()
    {
        var points = new[]
        {
            Point("bank-a", TimeSpan.Zero, 50, 500, 5),
            Point("bank-a", TimeSpan.FromHours(1), 55, 500, 5),
            Point("bank-a", TimeSpan.FromHours(2), 60, 500, 5),
        };

        var summary = BmsHistoryCalculations.CalculateToday(points, NowUtc);

        summary.SocRatePercentPerHour.Should().BeApproximately(5, 0.001);
        summary.TimeToFull.Should().Be(TimeSpan.FromHours(8));
        summary.TimeToEmpty.Should().BeNull();
    }

    [TestMethod]
    public void CalculateToday_ProvidesDischargeRateAndTimeToEmpty()
    {
        var points = new[]
        {
            Point("bank-a", TimeSpan.Zero, 60, -500, -5),
            Point("bank-a", TimeSpan.FromHours(1), 58, -500, -5),
            Point("bank-a", TimeSpan.FromHours(2), 56, -500, -5),
        };

        var summary = BmsHistoryCalculations.CalculateToday(points, NowUtc);

        summary.SocRatePercentPerHour.Should().BeApproximately(-2, 0.001);
        summary.TimeToFull.Should().BeNull();
        summary.TimeToEmpty.Should().Be(TimeSpan.FromHours(28));
    }

    [TestMethod]
    public void CalculateToday_DoesNotReportTimeToEmptyWhileFleetIsCharging()
    {
        var points = new[]
        {
            Point("bank-a", TimeSpan.FromHours(1), 50, 500, 5),
            Point("bank-a", TimeSpan.FromHours(2), 55, 500, 5),
        };

        var summary = BmsHistoryCalculations.CalculateToday(points, NowUtc);

        summary.PowerDirection.Should().Be(BmsPowerFlowDirection.Charging);
        summary.TimeToFull.Should().NotBeNull();
        summary.TimeToEmpty.Should().BeNull();
    }

    [TestMethod]
    public void CalculateToday_DoesNotReportTimeToFullWhileFleetIsDischarging()
    {
        var points = new[]
        {
            Point("bank-a", TimeSpan.FromHours(1), 60, -500, -5),
            Point("bank-a", TimeSpan.FromHours(2), 58, -500, -5),
        };

        var summary = BmsHistoryCalculations.CalculateToday(points, NowUtc);

        summary.PowerDirection.Should().Be(BmsPowerFlowDirection.Discharging);
        summary.TimeToFull.Should().BeNull();
        summary.TimeToEmpty.Should().NotBeNull();
    }

    [TestMethod]
    public void CalculateToday_IgnoresLongGapsWhenIntegrating()
    {
        var points = new[]
        {
            Point("bank-a", TimeSpan.Zero, 50, 1000, 10),
            Point("bank-a", TimeSpan.FromHours(12), 60, 1000, 10),
        };

        var summary = BmsHistoryCalculations.CalculateToday(points, NowUtc);

        summary.ChargeEnergyKwh.Should().Be(0);
        summary.ChargeAmpHours.Should().Be(0);
    }

    [TestMethod]
    public void AggregateFleet_AveragesPerBankThenSumsCurrentAndPower()
    {
        var points = new[]
        {
            new BmsHistoryPoint("a", "A", NowUtc, 50, 53.2, 10, 500, 24, 3, false),
            new BmsHistoryPoint("a", "A", NowUtc.AddMinutes(5), 52, 53.4, 12, 600, 25, 3, false),
            new BmsHistoryPoint("b", "B", NowUtc, 70, 53.0, -4, -200, 26, 4, false),
        };

        var aggregate = BmsHistoryCalculations.AggregateFleet(
            points, NowUtc, NowUtc.AddMinutes(15), TimeSpan.FromMinutes(15)).Single();

        aggregate.StateOfChargePercent.Should().BeApproximately(60.5, 0.001);
        aggregate.PackVoltageV.Should().BeApproximately(53.15, 0.001);
        aggregate.IntoPackCurrentA.Should().BeApproximately(7, 0.001);
        aggregate.IntoPackPowerW.Should().BeApproximately(350, 0.001);
    }

    [TestMethod]
    public void CalculateDailySummaries_ReportsEnergyAndVoltageExtremes()
    {
        var points = new[]
        {
            Point("bank-a", TimeSpan.Zero, 50, 1000, 10) with { PackVoltageV = 52.9 },
            Point("bank-a", TimeSpan.FromMinutes(5), 55, 1000, 10) with { PackVoltageV = 53.4 },
            Point("bank-a", TimeSpan.FromMinutes(10), 54, -500, -5) with { PackVoltageV = 53.1 },
        };

        var summary = BmsHistoryCalculations.CalculateDailySummaries(points, NowUtc, 1).Single();

        summary.ChargeEnergyKwh.Should().BeApproximately(1d / 12d, 0.001);
        summary.DischargeEnergyKwh.Should().BeApproximately(0.5d / 12d, 0.001);
        summary.HighVoltageV.Should().Be(53.4);
        summary.LowVoltageV.Should().Be(52.9);
    }

    [TestMethod]
    public void CalculateToday_UsesConfiguredLocalDayBoundary()
    {
        var phoenix = TimeZoneInfo.FindSystemTimeZoneById("America/Phoenix");
        var points = new[]
        {
            new BmsHistoryPoint("a", "A", new DateTime(2026, 8, 2, 6, 55, 0, DateTimeKind.Utc), 40, 53, 10, 1000, 24, 3, false),
            new BmsHistoryPoint("a", "A", new DateTime(2026, 8, 2, 7, 30, 0, DateTimeKind.Utc), 50, 53, 10, 1000, 24, 3, false),
            new BmsHistoryPoint("a", "A", new DateTime(2026, 8, 2, 7, 35, 0, DateTimeKind.Utc), 51, 53, 10, 1000, 24, 3, false),
        };

        var summary = BmsHistoryCalculations.CalculateToday(points, NowUtc, phoenix);

        summary.AverageSocPercent.Should().Be(51);
        summary.ChargeEnergyKwh.Should().BeApproximately(1d / 12d, 0.001);
    }

    private static BmsHistoryPoint Point(string alias, TimeSpan offset, double soc, double power, double current) =>
        new("AA:BB:CC:DD:EE:01", alias, NowUtc.Date.Add(offset), soc, 53.2, current, power, 25, 3, false);
}