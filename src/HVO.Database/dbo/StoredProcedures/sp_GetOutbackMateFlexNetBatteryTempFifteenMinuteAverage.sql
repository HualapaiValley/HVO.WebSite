
CREATE PROCEDURE [dbo].[sp_GetOutbackMateFlexNetBatteryTempFifteenMinuteAverage] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT                        
    MIN(DATEADD(MINUTE, FLOOR(DATEPART(minute, recordDateTime) / 15) * 15, DATEADD(MINUTE, DATEPART(MINUTE, recordDateTime) * -1, DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    CAST(AVG(batteryTemperatureC) as decimal(9, 2)) as batteryTemperatureC
  FROM 
    [dbo].[OutbackMateFlexNetRecords]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime), (FLOOR(DATEPART(minute, recordDateTime) / 15) * 15)
  ORDER BY
    recordDateTime ASC
END

GO

