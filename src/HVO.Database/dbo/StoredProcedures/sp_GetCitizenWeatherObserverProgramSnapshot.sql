
CREATE PROCEDURE [dbo].[sp_GetCitizenWeatherObserverProgramSnapshot]
  @iLastRecordDateTime datetimeoffset(7)
AS
BEGIN
  SELECT
    I.id,
    I.recordDateTime,
    I.outsideTemperature,
    I.outsideHumidity,
    null as hourlyRainAmount,
    I.dailyRainAmount,
    I.barometer,
    I.solarRadiation,
    OneMinuteAverage.windSpeed AS oneMinuteWindSpeed,
    OneMinuteAverage.windDirection AS oneMinuteWindDirection,
    FiveMinuteMax.windSpeed AS fiveMinuteWindSpeed
  FROM 
    [dbo].[DavisVantageProConsoleRecords_NEW] I
    OUTER APPLY
    (
      SELECT 
        AVG(windSpeed) AS windSpeed,
        AVG(windDirection) as windDirection
      FROM 
        [dbo].[DavisVantageProConsoleRecords_NEW]
      WHERE 
        recordDateTime BETWEEN DATEADD(MINUTE, -1, I.recordDateTime) AND I.recordDateTime
      ) OneMinuteAverage
    OUTER APPLY
    (
      SELECT TOP 1
        windSpeed
      FROM 
        [dbo].[DavisVantageProConsoleRecords_NEW]
      WHERE 
        recordDateTime BETWEEN DATEADD(MINUTE, -5, I.recordDateTime) AND I.recordDateTime
      ORDER BY
        windSpeed DESC
    ) FiveMinuteMax
  WHERE 
    I.recordDateTime = (SELECT TOP 1 recordDateTime FROM [DavisVantageProConsoleRecords_NEW] WHERE recordDateTime > @iLastRecordDateTime ORDER BY recordDateTime DESC)
END

GO

