using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Settings
{
    [Inject] private ILogger<Settings> Logger { get; set; } = default!;
    [Inject] private StationSettingsSnapshotStore SnapshotStore { get; set; } = default!;

    private StationSettings? _s;
    private DateTime? _settingsSavedAtUtc;
    private ArchiveCatchupStatus? _archiveStatus;
    private bool _settingsLoading;
    private bool _archiveLoading;
    private bool _archiveRunning;
    private bool _archiveError;
    private string? _archiveMessage;
    private string? _error;

    protected override void OnInitialized()
    {
        _ = LoadPageAsync();
    }

    private Task LoadPageAsync() => Task.WhenAll(LoadCachedSettingsAsync(), LoadArchiveStatusAsync());

    private async Task LoadCachedSettingsAsync()
    {
        _settingsLoading = true;
        _error = null;
        Logger.LogDebug("Loading cached station settings snapshot");
        try
        {
            var snapshot = await SnapshotStore.GetAsync();
            _s = snapshot?.Settings;
            _settingsSavedAtUtc = snapshot?.SavedAtUtc;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load cached station settings snapshot");
            _error = ex.Message;
        }
        finally
        {
            _settingsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RefreshLiveSettingsAsync()
    {
        _settingsLoading = true;
        _error = null;
        Logger.LogDebug("Refreshing live station settings and updating cached snapshot");
        try
        {
            var liveSettings = await Station.GetStationSettingsAsync();
            var snapshot = await SnapshotStore.SaveAsync(liveSettings);
            Station.ApplyStationSettings(snapshot.Settings);
            _s = snapshot.Settings;
            _settingsSavedAtUtc = snapshot.SavedAtUtc;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to refresh live station settings");
            _error = ex.Message;
        }
        finally
        {
            _settingsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadArchiveStatusAsync()
    {
        _archiveLoading = true;
        Logger.LogDebug("Loading archive catchup status");
        try
        {
            _archiveStatus = await Worker.GetArchiveCatchupStatusAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load archive catchup status");
            _archiveError = true;
            _archiveMessage = ex.Message;
        }
        finally
        {
            _archiveLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RunArchiveTopOffAsync()
    {
        _archiveRunning = true;
        _archiveError = false;
        _archiveMessage = null;

        try
        {
            int count = await Worker.RunArchiveTopOffAsync();
            await LoadArchiveStatusAsync();
            _archiveMessage = $"Archive top-off complete. Added {count} record{(count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Manual archive top-off failed");
            _archiveError = true;
            _archiveMessage = ex.Message;
        }
        finally
        {
            _archiveRunning = false;
        }
    }

    private static string FormatLag(TimeSpan? lag)
    {
        if (!lag.HasValue)
        {
            return "—";
        }

        if (lag.Value.TotalMinutes < 1)
        {
            return "< 1 min";
        }

        if (lag.Value.TotalHours < 1)
        {
            return $"{lag.Value.TotalMinutes:F0} min";
        }

        return $"{lag.Value.TotalHours:F1} hr";
    }

    private static string FormatCachedAt(DateTime? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    private static string RainBucketLabel(int t) => t switch
    {
        0 => "0.01 in",
        1 => "0.2 mm",
        2 => "0.1 mm",
        _ => t.ToString()
    };
}
