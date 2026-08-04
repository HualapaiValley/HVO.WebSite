using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Configuration;

public sealed class JkBmsDisplayTimeZoneResolver(IOptions<JkBmsOptions> options)
{
    private const string DefaultTimeZoneId = "America/Phoenix";
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone(options.Value.DisplayTimeZoneId);

    public string TimeZoneId => _timeZone.Id;

    public TimeZoneInfo TimeZone => _timeZone;

    public DateTime ConvertFromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _timeZone);

    public string Label => _timeZone.Id == "UTC" ? "UTC" : _timeZone.Id;

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var normalizedTimeZoneId = NormalizeTimeZoneId(timeZoneId) ?? DefaultTimeZoneId;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZoneId);
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
