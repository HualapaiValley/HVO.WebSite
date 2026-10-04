using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Models;
using HVO.WebSite.Themes.Components.Format;

namespace HVO.WebSite.v9.Services;

public sealed record DashboardSection<T>(T? Value, string? Error = null)
{
    public bool Succeeded => Error is null;
    public static DashboardSection<T> Failed(string section) => new(default, $"{section} could not be refreshed. Other available data is retained.");
}

public sealed record PowerDashboardCurrentResult(
    DashboardSection<PowerSystemSnapshot?> Snapshot,
    DashboardSection<PowerInverterDetailSnapshotResponse> Inverter,
    DashboardSection<PowerMpptDetailSnapshotResponse> Controller);

public sealed record PowerDashboardHistoryResult(DateTime WindowStartUtc, DashboardSection<PowerTelemetryHistoryResponse> History, DateTime? WindowEndUtc = null);

public interface IPowerDashboardQuery
{
    Task<PowerDashboardCurrentResult> GetCurrentAsync(CancellationToken cancellationToken);
    Task<PowerDashboardHistoryResult> GetHistoryAsync(DateTime windowStartUtc, CancellationToken cancellationToken);
    Task<PowerDashboardHistoryResult> GetHistoryAsync(DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken)
        => GetHistoryAsync(windowStartUtc, cancellationToken);
}

public sealed class PowerDashboardSettings
{
    public TimeSpan CurrentInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan HistoryInterval { get; init; } = TimeSpan.FromSeconds(60);
    public int HistoryHours { get; init; } = 6;
    public string InverterSourceId { get; init; } = "eg4-6500ex-a";
    public string ControllerSourceId { get; init; } = "eg4-mppt100-48hv-a";
    public PowerCompositionOptions Composition { get; init; } = new();
    public HvoDisplayTimeZone DisplayTimeZone { get; init; } = new();

    public static PowerDashboardSettings FromConfiguration(IConfiguration configuration, PowerCompositionOptions composition)
    {
        var currentSeconds = Math.Clamp(configuration.GetValue("PowerStatus:RefreshSeconds", 5), 1, 300);
        return new()
        {
            CurrentInterval = TimeSpan.FromSeconds(currentSeconds),
            HistoryInterval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("PowerStatus:HistoryRefreshSeconds", 60), currentSeconds, 3600)),
            HistoryHours = Math.Clamp(configuration.GetValue("PowerStatus:HistoryHours", 6), 1, 48),
            InverterSourceId = configuration["PowerStatus:Eg4InverterSourceId"] ?? "eg4-6500ex-a",
            ControllerSourceId = configuration["PowerStatus:Eg4MpptSourceId"] ?? "eg4-mppt100-48hv-a",
            Composition = composition,
            DisplayTimeZone = new(configuration["PowerStatus:DisplayTimeZoneId"]),
        };
    }
}

/// <summary>Each operation owns its providers and DbContext scope; the circuit never retains either.</summary>
public sealed class PowerDashboardQuery(
    IServiceScopeFactory scopeFactory,
    PowerDashboardSettings settings,
    ILogger<PowerDashboardQuery> logger,
    TimeProvider? clock = null) : IPowerDashboardQuery
{
    public async Task<PowerDashboardCurrentResult> GetCurrentAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        // Sequential within this operation because both providers can use the same scoped DbContext.
        var snapshot = await ReadAsync("Current power", () => scope.ServiceProvider.GetRequiredService<IPowerSystemSnapshotProvider>()
            .GetLatestAsync(ct: cancellationToken), cancellationToken);
        var inverter = await ReadAsync("Inverter detail", () => scope.ServiceProvider.GetRequiredService<IPowerInventoryConfigurationProvider>()
            .GetLatestInverterDetailAsync(settings.InverterSourceId, ct: cancellationToken), cancellationToken);
        var controller = await ReadAsync("Controller detail", () => scope.ServiceProvider.GetRequiredService<IPowerInventoryConfigurationProvider>()
            .GetLatestMpptDetailAsync(settings.ControllerSourceId, ct: cancellationToken), cancellationToken);
        return new(snapshot, inverter, controller);
    }

    public Task<PowerDashboardHistoryResult> GetHistoryAsync(DateTime windowStartUtc, CancellationToken cancellationToken)
        => GetHistoryAsync(windowStartUtc, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<PowerDashboardHistoryResult> GetHistoryAsync(DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var history = await ReadAsync("Power history", () => scope.ServiceProvider.GetRequiredService<IPowerInventoryConfigurationProvider>()
            .GetTelemetryWindowAsync(settings.Composition.ExpectedPvTrackerIds.Select(TrackerSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                [settings.InverterSourceId, settings.ControllerSourceId], windowStartUtc, windowEndUtc, cancellationToken), cancellationToken);
        return new(DateTime.SpecifyKind(windowStartUtc, DateTimeKind.Utc), history, windowEndUtc);
    }

    private async Task<DashboardSection<T>> ReadAsync<T>(string section, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await read();
            cancellationToken.ThrowIfCancellationRequested();
            return new(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Dashboard section {Section} failed after provider retries", section);
            return DashboardSection<T>.Failed(section);
        }
    }

    private static string TrackerSourceId(string trackerId)
    {
        var separator = trackerId.LastIndexOf('/');
        return separator > 0 ? trackerId[..separator] : trackerId;
    }
}
