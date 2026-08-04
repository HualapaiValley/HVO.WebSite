using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Configuration;

public sealed class JkBmsDisplayTimeZoneResolver(IOptions<JkBmsOptions> options)
{
    private const string DefaultTimeZoneId = "America/Phoenix";
    private readonly string _timeZoneId = NormalizeTimeZoneId(options.Value.DisplayTimeZoneId) ?? DefaultTimeZoneId;
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone(
        NormalizeTimeZoneId(options.Value.DisplayTimeZoneId) ?? DefaultTimeZoneId);

    public string TimeZoneId => _timeZoneId;

    public TimeZoneInfo TimeZone => _timeZone;

    public DateTime ConvertFromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _timeZone);

    public string Label => _timeZone.Id == "UTC" ? "UTC" : _timeZone.Id;

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
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
