
CREATE PROCEDURE [dbo].[sp_GetWeatherUndergroundSnapshot2]
  @iRecordDateTime datetimeoffset(7)
AS
BEGIN
  SELECT
    I.id,
    I.recordDateTime,
    I.insideTemperature,
    I.insideHumidity,
    I.windSpeed,
    I.windDirection,
    I.outsideTemperature,
    I.outsideHumidity,
    I.outsideDewpoint,
    I.dailyRainAmount,
    I.barometer,
    I.solarRadiation,
    TenMinMax.windSpeed AS TenMinuteMaxWindSpeed,
    TenMinMax.windDirection AS TenMinuteMaxWindDirection,
    OneMinMax.windSpeed AS OneMinuteMaxWindSpeed,
    OneMinMax.windDirection AS OneMinuteMaxWindDirection
  FROM 
    [dbo].[DavisVantageProConsoleRecords] I
    OUTER APPLY
    (
      SELECT TOP 1
        windSpeed,
        windDirection
      FROM 
        [dbo].[DavisVantageProConsoleRecords]
      WHERE 
        recordDateTime BETWEEN DATEADD(MINUTE, -10, I.recordDateTime) AND I.recordDateTime
      ORDER BY
        windSpeed DESC, recordDateTime DESC
      ) TenMinMax
    OUTER APPLY
    (
      SELECT TOP 1
        windSpeed,
        windDirection
      FROM 
        [dbo].[DavisVantageProConsoleRecords]
      WHERE 
        recordDateTime BETWEEN DATEADD(MINUTE, -1, I.recordDateTime) AND I.recordDateTime
      ORDER BY
        windSpeed DESC, recordDateTime DESC
    ) OneMinMax
  WHERE 
    I.recordDateTime = @iRecordDateTime
END

GO

