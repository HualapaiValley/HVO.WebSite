


CREATE PROCEDURE [dbo].[sp_GetWeatherUndergroundSnapshot_BACKFILL]
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
    rainAmountLast60Minute = (I.yearlyRainAmount - Prior60MinuteRainAmount.yearlyRainAmount),  
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

    OUTER APPLY 
    (
      SELECT TOP 1
        yearlyRainAmount
      FROM 
        [dbo].[DavisVantageProConsoleRecords]
      WHERE 
        recordDateTime BETWEEN DATEADD(MINUTE, -60, I.recordDateTime) AND I.recordDateTime
      ORDER BY
        recordDateTime 
    ) Prior60MinuteRainAmount
  WHERE 
    I.recordDateTime = @iRecordDateTime
END

GO

