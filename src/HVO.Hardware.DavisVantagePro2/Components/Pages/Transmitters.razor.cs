using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Transmitters
{
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
        try { _transmitters = await Station.GetTransmittersAsync(); }
        catch (Exception ex) { _loadError = ex.Message; }
        finally { _loading = false; }
    }

    private void BeginEdit(TransmitterConfig tx)
    {
        _editing = tx;
        _editType = Enum.TryParse<TransmitterType>(tx.TransmitterType, true, out var t) ? t : TransmitterType.Iss;
        _editRepeater   = tx.RepeaterId;
        _editExtraTemp  = tx.ExtraTemperatureId;
        _editExtraHumid = tx.ExtraHumidityId;
    }

    private async Task SaveEditAsync()
    {
        if (_editing is null) return;
        try
        {
            await Station.SetTransmitterAsync(
                _editing.Channel, _editType,
                _editExtraTemp, _editExtraHumid,
                string.IsNullOrWhiteSpace(_editRepeater) ? null : _editRepeater);
            _msg = $"Channel {_editing.Channel} saved."; _isError = false;
            await LoadAsync();
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }
}
