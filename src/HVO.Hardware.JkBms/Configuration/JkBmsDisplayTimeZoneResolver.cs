using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Configuration;

public sealed class JkBmsDisplayTimeZoneResolver(IOptions<JkBmsOptions> options)
{
    private const string DefaultTimeZoneId = "America/Phoenix";

    public string TimeZoneId => NormalizeTimeZoneId(options.Value.DisplayTimeZoneId) ?? DefaultTimeZoneId;

    public TimeZoneInfo TimeZone
    {
        get
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
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
    }

    public DateTime ConvertFromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public string Label => TimeZone.Id == "UTC" ? "UTC" : TimeZone.Id;

    private static string? NormalizeTimeZoneId(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
}
