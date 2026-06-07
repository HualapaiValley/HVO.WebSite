using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Configuration;

public sealed class KasaDisplayTimeZoneResolver(IOptions<KasaGatewayOptions> options)
{
    private const string UtcTimeZoneId = "UTC";

    public string GatewayTimeZoneId => NormalizeTimeZoneId(options.Value.DisplayTimeZoneId) ?? UtcTimeZoneId;

    public string ResolveTimeZoneId(KasaDeviceConfig? device) =>
        NormalizeTimeZoneId(device?.DisplayTimeZoneId) ?? GatewayTimeZoneId;

    public string ResolveTimeZoneId(string? deviceTimeZoneId) =>
        NormalizeTimeZoneId(deviceTimeZoneId) ?? GatewayTimeZoneId;

    public DateTimeOffset ConvertFromUtc(DateTimeOffset utc, string? deviceTimeZoneId = null) =>
        TimeZoneInfo.ConvertTime(utc.ToUniversalTime(), ResolveTimeZone(deviceTimeZoneId));

    public string GetLabel(string? deviceTimeZoneId = null)
    {
        var timeZone = ResolveTimeZone(deviceTimeZoneId);
        return timeZone.Id == UtcTimeZoneId ? "UTC" : timeZone.Id;
    }

    private TimeZoneInfo ResolveTimeZone(string? deviceTimeZoneId)
    {
        var timeZoneId = ResolveTimeZoneId(deviceTimeZoneId);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string? NormalizeTimeZoneId(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
}