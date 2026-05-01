using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Transmitters
{
    [Inject] private ILogger<Transmitters> Logger { get; set; } = default!;

    private IReadOnlyList<TransmitterConfig>? _transmitters;
    private bool _loading;
    private string? _loadError;
    private string? _msg;
    private bool _isError;

    private TransmitterConfig? _editing;
    private TransmitterType _editType;
    private string? _editRepeater;
    private int? _editExtraTemp;
    private int? _editExtraHumid;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _loadError = null; _editing = null;
        Logger.LogDebug("Loading transmitter configuration");
        try { _transmitters = await Station.GetTransmittersAsync(); }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load transmitter configuration");
            _loadError = ex.Message;
        }
        finally { _loading = false; }
    }

    private void BeginEdit(TransmitterConfig tx)
    {
        _editing = tx;
        _editType = Enum.TryParse<TransmitterType>(tx.TransmitterType, true, out var t) ? t : TransmitterType.Iss;
        _editRepeater = tx.RepeaterId;
        _editExtraTemp = tx.ExtraTemperatureId;
        _editExtraHumid = tx.ExtraHumidityId;
    }

    private async Task SaveEditAsync()
    {
        if (_editing is null) return;
        Logger.LogInformation("Saving transmitter channel {Channel}: type={Type}",
            _editing.Channel, _editType);
        try
        {
            await Station.SetTransmitterAsync(
                _editing.Channel, _editType,
                _editExtraTemp, _editExtraHumid,
                string.IsNullOrWhiteSpace(_editRepeater) ? null : _editRepeater);
            _msg = $"Channel {_editing.Channel} saved."; _isError = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save transmitter channel {Channel}", _editing.Channel);
            _msg = ex.Message; _isError = true;
        }
    }
}
