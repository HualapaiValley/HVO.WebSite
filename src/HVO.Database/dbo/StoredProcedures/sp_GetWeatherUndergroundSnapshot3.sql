

-- Batch submitted through debugger: Alter dbo.sp_GetWeatherUndergroundSnapshot3|9|0|8bb844c3-2459-4397-88c9-87823d883a65MSSQL__/sqlserver.express_is.net/HualapaiValleyObservatory/True/SqlProcedure/Alter dbo.sp_GetWeatherUndergroundSnapshot3.sql

-- Batch submitted through debugger: SQLQuery11.sql|7|0|C:\Users\Administrator.EXPRESS-IS\AppData\Local\Temp\2\~vs23B0.sql

CREATE PROCEDURE [dbo].[sp_GetWeatherUndergroundSnapshot3]
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
    I.recordDateTime = (SELECT TOP 1 recordDateTime FROM [DavisVantageProConsoleRecords] WHERE recordDateTime > @iRecordDateTime ORDER BY recordDateTime DESC)
END

GO

