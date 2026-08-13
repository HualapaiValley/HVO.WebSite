namespace HVO.WebSite.Themes.Components.Format;

public sealed class HvoDisplayTimeZone
{
    public const string DefaultTimeZoneId = "America/Phoenix";

    public HvoDisplayTimeZone(string? timeZoneId = null)
    {
        var normalized = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        try
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch (TimeZoneNotFoundException)
        {
            TimeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            TimeZone = TimeZoneInfo.Utc;
        }
    }

    public TimeZoneInfo TimeZone { get; }

    public string Label => TimeZone.Id;

    /// <summary>Converts UTC values; persistence-style Unspecified values are intentionally interpreted as UTC.</summary>
    public DateTime ConvertFromUtc(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Local)
            throw new ArgumentException("A local DateTime cannot be converted as UTC.", nameof(utc));
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(normalized, TimeZone);
    }

    public static bool IsValid(string? timeZoneId)
    {
        var normalized = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
