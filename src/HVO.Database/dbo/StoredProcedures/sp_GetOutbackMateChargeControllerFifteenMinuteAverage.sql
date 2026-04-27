



CREATE PROCEDURE [dbo].[sp_GetOutbackMateChargeControllerFifteenMinuteAverage] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT 
    MIN(DATEADD(MINUTE, FLOOR(DATEPART(minute, recordDateTime) / 15) * 15, DATEADD(MINUTE, DATEPART(MINUTE, recordDateTime) * -1, DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    CAST(AVG(pvAmps) as tinyint) as pvAmps,
    CAST(AVG(pvVoltage) as tinyint) as pvVoltage,
    AVG(CAST(pvAmps as smallint) * pvVoltage) as pvWatts,
    CAST(AVG(chargerAmps) as decimal(9, 2)) as chargerAmps,
    CAST(AVG(chargerVoltage) as decimal(9, 2)) as chargerVoltage,
    CAST(AVG(chargerAmps * chargerVoltage) as decimal(9, 2)) as chargerWatts,
    MAX(dailyAmpHoursProduced) as dailyAmpHoursProduced,
    MAX(dailyWattHoursProduced) as dailyWattHoursProduced,
    CAST(FLOOR(AVG(chargerMode)) as tinyint) as chargerMode
  FROM 
    [dbo].[OutbackMateChargeControllerRecords]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime), (FLOOR(DATEPART(minute, recordDateTime) / 15) * 15)
  ORDER BY
    recordDateTime ASC

END

GO

