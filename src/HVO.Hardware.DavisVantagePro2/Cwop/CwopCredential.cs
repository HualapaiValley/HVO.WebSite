namespace HVO.Hardware.DavisVantagePro2.Cwop;

internal sealed class CwopCredential
{
    private string? passcode;

    public void Initialize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed != "-1"
            && (trimmed.Length is < 1 or > 5
                || !trimmed.All(char.IsAsciiDigit)
                || !int.TryParse(trimmed, out var parsed)
                || parsed > 32767))
            throw new InvalidOperationException("The CWOP APRS-IS passcode is invalid.");
        passcode = trimmed;
    }

    public string GetRequired() => passcode
        ?? throw new InvalidOperationException("The CWOP APRS-IS passcode was not initialized.");
}
