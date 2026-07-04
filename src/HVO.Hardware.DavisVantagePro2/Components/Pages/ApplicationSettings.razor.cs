using System.Net.Http.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Workers;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class ApplicationSettings : IDisposable
{
    [Inject] private ILogger<ApplicationSettings> Logger { get; set; } = default!;
    [Inject] private OutboxForwarder Forwarder { get; set; } = default!;
    [Inject] private IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] private IOptions<OutboxOptions> OutboxOptions { get; set; } = default!;

    private int _batchSize;
    private int _sweepIntervalSeconds;
    private int _configuredBatchSize;
    private int _configuredSweepIntervalSeconds;
    private bool _dirty;
    private bool _isOverride;
    private string? _lastSaveMessage;

    protected override void OnInitialized()
    {
        Forwarder.SweptCompleted += OnStateChanged;
        _configuredBatchSize = 50;
        _configuredSweepIntervalSeconds = 5;
        _batchSize = _configuredBatchSize;
        _sweepIntervalSeconds = _configuredSweepIntervalSeconds;
    }

    public void Dispose()
    {
        Forwarder.SweptCompleted -= OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private string FormatLastSweep()
    {
        if (Forwarder.LastSentAt is { } lastSent)
            return HvoFormat.Timestamp(lastSent, "MMM d, yyyy - h:mm tt");
        return "--";
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", OutboxOptions.Value.ApiKey);
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { batchSize = _batchSize, sweepIntervalSeconds = _sweepIntervalSeconds });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OutboxSettingsResponse>();
                _isOverride = result?.IsOverride ?? false;
                _lastSaveMessage = _isOverride
                    ? $"Runtime override active: batch={_batchSize}, sweep={_sweepIntervalSeconds}s"
                    : $"Using defaults: batch={_batchSize}, sweep={_sweepIntervalSeconds}s";
                _dirty = false;
                Logger.LogInformation("Outbox runtime settings saved: batch={BatchSize}, sweep={SweepIntervalSeconds}s",
                    _batchSize, _sweepIntervalSeconds);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _lastSaveMessage = $"Save failed: {error}";
                Logger.LogWarning("Failed to save outbox settings: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _lastSaveMessage = $"Save error: {ex.Message}";
            Logger.LogError(ex, "Error saving outbox runtime settings");
        }

        StateHasChanged();
    }

    private async Task ResetSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", OutboxOptions.Value.ApiKey);
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { reset = true });

            if (response.IsSuccessStatusCode)
            {
                _batchSize = _configuredBatchSize;
                _sweepIntervalSeconds = _configuredSweepIntervalSeconds;
                _isOverride = false;
                _dirty = false;
                _lastSaveMessage = "Reset to configured defaults.";
                Logger.LogInformation("Outbox runtime settings reset to defaults");
            }
        }
        catch (Exception ex)
        {
            _lastSaveMessage = $"Reset error: {ex.Message}";
            Logger.LogError(ex, "Error resetting outbox runtime settings");
        }

        StateHasChanged();
    }
}
