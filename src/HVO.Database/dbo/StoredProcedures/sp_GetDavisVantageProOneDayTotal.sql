


CREATE PROCEDURE [dbo].[sp_GetDavisVantageProOneDayTotal] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT 
    MIN(DATEADD(HOUR, DATEPART(HOUR, recordDateTime) * -1, DATEADD(MINUTE, DATEPART(MINUTE, recordDateTime) * -1, DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    CAST(AVG(solarRadiation) as smallint) as solarRadiation,
    MAX(dailyRainAmount) as dailyRainAmount,
    MAX(monthlyRainAmount) as monthlyRainAmount,
	  MAX(yearlyRainAmount) as yearlyRainAmount,
    CAST(MIN(consoleBatteryVoltage) as decimal(9, 2)) as consoleBatteryVoltage,
	  MAX(sunriseTime) as sunriseTime,
    MAX(sunsetTime) as sunsetTime,
	  MAX(dailyETAmount) as dailyETAmount,
	  MAX(monthlyETAmount) as monthlyETAmount,
	  MAX(yearlyETAmount) as yearlyETAmount
  FROM 
    [dbo].[DavisVantageProConsoleRecords]
  WHERE
  (recordDateTime >= @iRecordDateTimeStart) AND 
  (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    --recordDateTimeYear, recordDateTimeMonth, recordDateTimeDay
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime)
  ORDER BY
    recordDateTime DESC

END

GO

