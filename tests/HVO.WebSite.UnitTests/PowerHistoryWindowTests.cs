using FluentAssertions;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerHistoryWindowTests
{
    [TestMethod]
    public async Task Session_ClosesRequestedWindowBeforeProviderDelay_AndPreservesThatWindowForPresentation()
    {
        var end = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(end));
        await using var session = new PowerDashboardSession(new AdvancingQuery(clock), clock, new() { HistoryHours = 48 }, NullLogger<PowerDashboardSession>.Instance);
        await session.StartAsync();
        clock.GetUtcNow().UtcDateTime.Should().Be(end.AddMinutes(1));
        session.HistoryWindowEndUtc.Should().Be(end);
        session.HistoryWindowStartUtc.Should().Be(end.AddHours(-48));
        var view = new PowerDashboardHistoryPresenter(session.History, new(), session.HistoryWindowStartUtc, session.HistoryWindowEndUtc, new());
        view.TimesUtc.Should().HaveCount(577).And.EndWith(end);
    }

    [TestMethod]
    public async Task Query_PassesExplicitEndToProvider_AndDoesNotReplaceItWithLaterClockTime()
    {
        var end = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(end));
        var provider = new AdvancingProvider(clock);
        await using var services = new ServiceCollection().AddScoped<IPowerInventoryConfigurationProvider>(_ => provider).BuildServiceProvider();
        var query = new PowerDashboardQuery(services.GetRequiredService<IServiceScopeFactory>(), new(), NullLogger<PowerDashboardQuery>.Instance, clock);
        var result = await query.GetHistoryAsync(end.AddHours(-48), end, default);
        result.WindowStartUtc.Should().Be(end.AddHours(-48)); result.WindowEndUtc.Should().Be(end);
        provider.Start.Should().Be(result.WindowStartUtc); provider.End.Should().Be(end);
        result.History.Succeeded.Should().BeTrue(); clock.GetUtcNow().UtcDateTime.Should().Be(end.AddMinutes(1));
    }

    private sealed class AdvancingQuery(FakeTimeProvider clock) : IPowerDashboardQuery
    {
        public Task<PowerDashboardCurrentResult> GetCurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(new PowerDashboardCurrentResult(new(null), new(new()), new(new())));
        public Task<PowerDashboardHistoryResult> GetHistoryAsync(DateTime windowStartUtc, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The session must supply both UTC window boundaries.");
        public Task<PowerDashboardHistoryResult> GetHistoryAsync(DateTime start, DateTime end, CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            return Task.FromResult(new PowerDashboardHistoryResult(start, new(PowerTelemetryHistoryResponse.Empty), end));
        }
    }
    private sealed class AdvancingProvider(FakeTimeProvider clock) : IPowerInventoryConfigurationProvider
    {
        public DateTime Start { get; private set; }
        public DateTime End { get; private set; }
        public Task<PowerTelemetryHistoryResponse> GetTelemetryWindowAsync(IReadOnlyCollection<string> mpptSourceIds, IReadOnlyCollection<string> batterySourceIds, DateTime sinceUtc, DateTime untilUtc, CancellationToken ct = default)
        {
            Start = sinceUtc; End = untilUtc; clock.Advance(TimeSpan.FromMinutes(1));
            return Task.FromResult(PowerTelemetryHistoryResponse.Empty);
        }
        public Task<PowerTelemetryHistoryResponse> GetRecentTelemetryAsync(IReadOnlyCollection<string> mpptSourceIds, IReadOnlyCollection<string> batterySourceIds, DateTime sinceUtc, CancellationToken ct = default)
            => throw new InvalidOperationException("The production query must preserve its explicit UTC window end.");
        public Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(string sourceId = "solarassistant-total", int staleAfterMinutes = 1440, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PowerInverterDetailSnapshotResponse> GetLatestInverterDetailAsync(string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PowerMpptDetailSnapshotResponse> GetLatestMpptDetailAsync(string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
