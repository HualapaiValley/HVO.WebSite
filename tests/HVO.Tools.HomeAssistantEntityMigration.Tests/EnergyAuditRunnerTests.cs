using System.Text.Json;
using FluentAssertions;

namespace HVO.Tools.HomeAssistantEntityMigration.Tests;

[TestClass]
[DoNotParallelize]
public sealed class EnergyAuditRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Audit_ConsistentCurrentValuesAndFreshStatistics_PrintsPassingTable()
    {
        await using var client = AuditClient.Valid(Now);
        var runner = new EnergyAuditRunner(client, new FixedTimeProvider(Now));
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            await runner.AuditAsync(CancellationToken.None);
        }
        finally
        {
            Console.SetOut(original);
        }

        output.ToString().Should().Contain("America/Phoenix")
            .And.Contain("6500EX PV")
            .And.Contain("Combined solar")
            .And.NotContain("FAIL");
        client.DailyRequests.Should().BeEquivalentTo(client.Changes.Keys);
        client.RecentRequest.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Audit_MissingOrStaleStatistics_AreRejected()
    {
        await using var missing = AuditClient.Valid(Now);
        missing.Recent.Remove("sensor.hvo_external_mppt_pv_energy");
        await using var stale = AuditClient.Valid(Now);
        stale.SetRecentEnd(Now - TimeSpan.FromMinutes(16));

        var missingAct = () => new EnergyAuditRunner(missing, new FixedTimeProvider(Now)).AuditAsync(CancellationToken.None);
        var staleAct = () => new EnergyAuditRunner(stale, new FixedTimeProvider(Now)).AuditAsync(CancellationToken.None);

        await missingAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no recent statistics*external_mppt*");
        await staleAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*statistics are stale*");
    }

    [TestMethod]
    public async Task Audit_InvalidTimezoneOrNonnumericCurrentValue_IsRejected()
    {
        await using var invalidTimezone = AuditClient.Valid(Now);
        invalidTimezone.TimeZone = "Not/A_Timezone";
        await using var nonnumeric = AuditClient.Valid(Now);
        nonnumeric.States["sensor.hvo_total_pv_energy_daily"] = State("sensor.hvo_total_pv_energy_daily", "unavailable");

        var timezoneAct = () => new EnergyAuditRunner(invalidTimezone, new FixedTimeProvider(Now)).AuditAsync(CancellationToken.None);
        var numericAct = () => new EnergyAuditRunner(nonnumeric, new FixedTimeProvider(Now)).AuditAsync(CancellationToken.None);

        await timezoneAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unknown time_zone*");
        await numericAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*finite numeric current value*hvo_total_pv_energy_daily*");
    }

    [TestMethod]
    public async Task Audit_MaterialSourceOrCombinedDiscrepancy_IsRejected()
    {
        await using var sourceMismatch = AuditClient.Valid(Now);
        sourceMismatch.Changes["sensor.hvo_external_mppt_pv_energy"] = 5;
        await using var combinedMismatch = AuditClient.Valid(Now);
        combinedMismatch.States["sensor.hvo_total_pv_energy_daily"] = State("sensor.hvo_total_pv_energy_daily", "20");

        var sourceAct = () => new EnergyAuditRunner(sourceMismatch, new FixedTimeProvider(Now)).AuditAsync(CancellationToken.None);
        var combinedAct = () => new EnergyAuditRunner(combinedMismatch, new FixedTimeProvider(Now)).AuditAsync(CancellationToken.None);

        await sourceAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*External MPPT PV*likely utility-meter gap loss*source=sensor.hvo_external_mppt_pv_energy*meter=sensor.hvo_external_mppt_pv_energy_daily*");
        await combinedAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Combined solar*");
    }

    private static JsonElement State(string entityId, string state) => JsonSerializer.SerializeToElement(new
    {
        entity_id = entityId,
        state,
        attributes = new { },
    });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AuditClient : IHomeAssistantRegistryClient
    {
        public string Version => "2026.8.1";
        public string TimeZone { get; set; } = "America/Phoenix";
        public Dictionary<string, JsonElement> States { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> Changes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement[]> Recent { get; } = new(StringComparer.Ordinal);
        public List<string> DailyRequests { get; } = [];
        public IReadOnlyList<string>? RecentRequest { get; private set; }

        public static AuditClient Valid(DateTimeOffset now)
        {
            var client = new AuditClient();
            client.Add("sensor.hvo_6500ex_pv_energy_daily", "10", "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_21xpv_x5fenergy_x5ftotal", 10, now);
            client.Add("sensor.hvo_external_mppt_pv_energy_daily", "2", "sensor.hvo_external_mppt_pv_energy", 2, now);
            client.Add("sensor.hvo_6500ex_ac_load_energy_daily", "5", "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_23xload_x5fenergy_x5ftotal", 5, now);
            client.Add("sensor.hvo_battery_charge_energy_daily", "3", "sensor.hvo_smartshunt_battery_charge_energy", 3, now);
            client.Add("sensor.hvo_battery_discharge_energy_daily", "1", "sensor.hvo_smartshunt_battery_discharge_energy", 1, now);
            client.States["sensor.hvo_total_pv_energy_daily"] = State("sensor.hvo_total_pv_energy_daily", "12");
            return client;
        }

        public void SetRecentEnd(DateTimeOffset end)
        {
            foreach (var statisticId in Recent.Keys.ToArray())
                Recent[statisticId] = [Row(end)];
        }

        private void Add(string currentId, string current, string statisticId, double change, DateTimeOffset now)
        {
            States[currentId] = State(currentId, current);
            Changes[statisticId] = change;
            Recent[statisticId] = [Row(now - TimeSpan.FromMinutes(5))];
        }

        private static JsonElement Row(DateTimeOffset end) => JsonSerializer.SerializeToElement(new
        {
            start = end.AddMinutes(-5).ToUnixTimeMilliseconds(),
            end = end.ToUnixTimeMilliseconds(),
            sum = 1,
        });

        public Task<JsonElement> GetConfigAsync(CancellationToken cancellationToken) =>
            Task.FromResult(JsonSerializer.SerializeToElement(new { time_zone = TimeZone }));
        public Task<JsonElement[]> ListStatesAsync(CancellationToken cancellationToken) => Task.FromResult(States.Values.ToArray());
        public Task<JsonElement> GetDailyStatisticAsync(string statisticId, CancellationToken cancellationToken)
        {
            DailyRequests.Add(statisticId);
            return Task.FromResult(Changes.TryGetValue(statisticId, out var change)
                ? JsonSerializer.SerializeToElement(new { change })
                : JsonSerializer.SerializeToElement(new { }));
        }

        public Task<JsonElement> GetStatisticsDuringPeriodAsync(
            IReadOnlyList<string> statisticIds,
            DateTimeOffset start,
            DateTimeOffset end,
            CancellationToken cancellationToken)
        {
            RecentRequest = statisticIds.ToArray();
            return Task.FromResult(JsonSerializer.SerializeToElement(Recent));
        }

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<JsonElement[]> ListEntitiesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> FindRelatedAsync(string entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CreateBackupAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JsonElement>> GetLovelaceConfigurationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> RenameAsync(string sourceEntityId, string targetEntityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement[]> ListStatisticIdsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement?> GetEnergyPreferencesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> SaveEnergyPreferencesAsync(IReadOnlyList<JsonElement> energySources, IReadOnlyList<JsonElement> deviceConsumption, IReadOnlyList<JsonElement> waterConsumption, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> ValidateEnergyAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
