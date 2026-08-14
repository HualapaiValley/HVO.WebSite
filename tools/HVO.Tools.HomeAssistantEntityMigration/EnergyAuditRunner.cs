using System.Globalization;
using System.Text.Json;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal sealed class EnergyAuditRunner(
    IHomeAssistantRegistryClient client,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan FreshnessLimit = TimeSpan.FromMinutes(15);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private static readonly AuditMetric[] Metrics =
    [
        new("6500EX PV", "sensor.hvo_6500ex_pv_energy_daily", "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_21xpv_x5fenergy_x5ftotal", 0.1, 9000),
        new("External MPPT PV", "sensor.hvo_external_mppt_pv_energy_daily", "sensor.hvo_external_mppt_pv_energy", 0.001, 5000),
        new("6500EX AC load", "sensor.hvo_6500ex_ac_load_energy_daily", "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_23xload_x5fenergy_x5ftotal", 0.1, 6000),
        new("Battery charge", "sensor.hvo_battery_charge_energy_daily", "sensor.hvo_smartshunt_battery_charge_energy", 0.001, 12000),
        new("Battery discharge", "sensor.hvo_battery_discharge_energy_daily", "sensor.hvo_smartshunt_battery_discharge_energy", 0.001, 12000),
    ];

    public async Task AuditAsync(CancellationToken cancellationToken)
    {
        var config = await client.GetConfigAsync(cancellationToken);
        var timeZoneId = config.TryGetProperty("time_zone", out var timeZone) ? timeZone.GetString() : null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new InvalidOperationException("Home Assistant get_config returned no time_zone.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException($"Home Assistant returned an unknown time_zone: {timeZoneId}.", exception);
        }

        var states = (await client.ListStatesAsync(cancellationToken))
            .ToDictionary(state => state.GetProperty("entity_id").GetString()!, StringComparer.Ordinal);
        var requiredCurrent = Metrics.Select(metric => metric.CurrentEntity).ToArray();
        var missing = requiredCurrent.Where(entityId => !states.ContainsKey(entityId)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Energy audit current entities are missing: {string.Join(", ", missing)}");

        var now = clock.GetUtcNow();
        var statisticIds = Metrics.Select(metric => metric.StatisticEntity).ToArray();
        var recent = await client.GetStatisticsDuringPeriodAsync(
            statisticIds, now - TimeSpan.FromMinutes(20), now, cancellationToken);

        Console.WriteLine("Home Assistant Energy local-day audit ({0})", timeZoneId);
        Console.WriteLine("{0,-20} {1,9} {2,9} {3,9} {4,9}  {5}", "Metric", "Current", "Stats", "Delta", "Tolerance", "Result");
        var failures = new List<string>();
        foreach (var metric in Metrics)
        {
            var current = RequiredNumber(states[metric.CurrentEntity], metric.CurrentEntity);
            var daily = await client.GetDailyStatisticAsync(metric.StatisticEntity, cancellationToken);
            if (!daily.TryGetProperty("change", out var changeElement)
                || changeElement.ValueKind != JsonValueKind.Number
                || !changeElement.TryGetDouble(out var change)
                || !double.IsFinite(change))
                throw new InvalidOperationException($"Energy audit has no numeric local-day statistics change for {metric.StatisticEntity}.");
            var age = StatisticAge(recent, metric.StatisticEntity, now);
            if (age > FreshnessLimit)
                throw new InvalidOperationException($"Energy audit statistics are stale for {metric.StatisticEntity}: {age.TotalMinutes:F1} minutes.");
            var tolerance = metric.BaseToleranceKwh + (metric.MaximumPowerWatts / 1000d * age.TotalHours);
            var delta = current - change;
            var passed = Math.Abs(delta) <= tolerance;
            Console.WriteLine("{0,-20} {1,9:F3} {2,9:F3} {3,9:F3} {4,9:F3}  {5}",
                metric.Label, current, change, delta, tolerance, passed ? "OK" : "FAIL");
            if (!passed)
            {
                failures.Add(change - current > tolerance
                    ? $"{metric.Label}: likely utility-meter gap loss after unavailable source intervals; "
                        + $"recorder change exceeds meter (source={metric.StatisticEntity}, meter={metric.CurrentEntity})"
                    : $"{metric.Label} (source={metric.StatisticEntity}, meter={metric.CurrentEntity})");
            }
        }

        const string combinedEntity = "sensor.hvo_total_pv_energy_daily";
        if (!states.TryGetValue(combinedEntity, out var combinedState))
        {
            Console.WriteLine("{0,-20} {1}", "Combined solar", "MISSING");
            failures.Add($"Combined solar ({combinedEntity} missing)");
        }
        else
        {
            var combined = RequiredNumber(combinedState, combinedEntity);
            var solarSources = RequiredNumber(states[Metrics[0].CurrentEntity], Metrics[0].CurrentEntity)
                + RequiredNumber(states[Metrics[1].CurrentEntity], Metrics[1].CurrentEntity);
            var combinedDelta = combined - solarSources;
            const double combinedTolerance = 0.002;
            var combinedPassed = Math.Abs(combinedDelta) <= combinedTolerance;
            Console.WriteLine("{0,-20} {1,9:F3} {2,9:F3} {3,9:F3} {4,9:F3}  {5}",
                "Combined solar", combined, solarSources, combinedDelta, combinedTolerance, combinedPassed ? "OK" : "FAIL");
            if (!combinedPassed)
                failures.Add("Combined solar");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException($"Home Assistant Energy local-day audit failed: {string.Join(", ", failures)}.");
    }

    private static double RequiredNumber(JsonElement state, string entityId)
    {
        var value = state.GetProperty("state").GetString();
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new InvalidOperationException($"Energy audit entity does not have a finite numeric current value: {entityId}.");
        return number;
    }

    private static TimeSpan StatisticAge(JsonElement recent, string statisticId, DateTimeOffset now)
    {
        if (!recent.TryGetProperty(statisticId, out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
            throw new InvalidOperationException($"Energy audit has no recent statistics for {statisticId}.");
        var latestEnd = rows.EnumerateArray().Select(row => row.GetProperty("end").GetInt64()).Max();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(latestEnd);
        var age = now - end;
        if (age < TimeSpan.FromMinutes(-1))
            throw new InvalidOperationException($"Energy audit statistics end time is in the future for {statisticId}.");
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    private sealed record AuditMetric(
        string Label,
        string CurrentEntity,
        string StatisticEntity,
        double BaseToleranceKwh,
        double MaximumPowerWatts);
}
