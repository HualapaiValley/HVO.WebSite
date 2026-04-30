namespace HVO.WebSite.v9;

/// <summary>
/// Well-known scope claim values used by API key authorization policies.
/// Assign these as ApiKeyClaim rows with ClaimType = "scope".
/// </summary>
public static class ApiScopes
{
    public const string WeatherIngest = "ingest:weather";
    public const string ImageIngest = "ingest:images";
    public const string PowerIngest = "ingest:power";
    public const string BmsIngest = "ingest:bms";
    public const string WeatherRead = "read:weather";
    public const string ApiRead = "read:api";
}
