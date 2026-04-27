


CREATE PROCEDURE [dbo].[sp_GetDavisVantageProFiveMinuteAverage] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT 
--    MIN(DATEADD(MINUTE, FLOOR(recordDateTimeMinute / 5) * 5, DATEADD(MINUTE, DATEPART(MINUTE, recordDateTime) * -1, DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))))) as recordDateTime,
    MIN(DATEADD(MINUTE, FLOOR(DATEPART(minute, recordDateTime) / 5) * 5, DATEADD(MINUTE, DATEPART(MINUTE, recordDateTime) * -1, DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    CAST(AVG(barometer) as decimal(9, 2)) as barometer,
    CAST(AVG(insideTemperature) as decimal(9, 2)) as insideTemperature,
    CAST(AVG(insideHumidity) as tinyint) as insideHumidity,
    CAST(AVG(outsideTemperature) as decimal(9, 2)) as outsideTemperature,
    CAST(AVG(outsideHumidity) as tinyint) as outsideHumidity,
    CAST(AVG(windSpeed) as tinyint) as windSpeed,
    CAST(MIN(windSpeed) as tinyint) as windSpeedLow,
    CAST(MAX(windSpeed) as tinyint) as windSpeedHigh,
    CAST(AVG(windDirection) as smallint) as windDirection,
    CAST(AVG(rainRate) as decimal(9, 2)) as rainRate,
    CAST(AVG(uvIndex) as tinyint) as uvIndex,
    CAST(AVG(solarRadiation) as smallint) as solarRadiation,
    CAST(AVG(stormRain) as decimal(9, 2)) as stormRain,
	  MIN(stormStartDate) as stormStartDate,
    MAX(dailyRainAmount) as dailyRainAmount,
    MAX(monthlyRainAmount) as monthlyRainAmount,
	  MAX(yearlyRainAmount) as yearlyRainAmount,
    CAST(AVG(consoleBatteryVoltage) as decimal(9, 2)) as consoleBatteryVoltage,
	  MAX(sunriseTime) as sunriseTime,
    MAX(sunsetTime) as sunsetTime,
	  MAX(dailyETAmount) as dailyETAmount,
	  MAX(monthlyETAmount) as monthlyETAmount,
	  MAX(yearlyETAmount) as yearlyETAmount,
	  CAST(AVG(outsideHeatIndex) as decimal(9, 2)) as outsideHeatIndex,
	  CAST(AVG(outsideWindChill) as decimal(9, 2)) as outsideWindChill,
	  CAST(AVG(outsideDewpoint) as decimal(9, 2)) as outsideDewpoint
  FROM 
    [dbo].[DavisVantageProConsoleRecords_NEW]
  WHERE
  (recordDateTime >= @iRecordDateTimeStart) AND 
  (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    --recordDateTimeYear, recordDateTimeMonth, recordDateTimeDay, recordDateTimeHour, (FLOOR(recordDateTimeMinute / 5) * 5)
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime), (FLOOR(DATEPART(minute, recordDateTime) / 5) * 5)
  ORDER BY
    recordDateTime ASC

END

GO

