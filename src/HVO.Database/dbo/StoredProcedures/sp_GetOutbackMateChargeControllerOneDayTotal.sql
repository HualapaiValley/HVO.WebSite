




CREATE PROCEDURE [dbo].[sp_GetOutbackMateChargeControllerOneDayTotal] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT 
    MIN(DATEADD(HOUR, DATEPART(HOUR, recordDateTime) * -1, DATEADD(MINUTE, DATEPART(MINUTE, recordDateTime) * -1, DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    MAX(dailyAmpHoursProduced) as dailyAmpHoursProduced,
    MAX(dailyWattHoursProduced) as dailyWattHoursProduced
  FROM 
    [dbo].[OutbackMateChargeControllerRecords]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime)
  ORDER BY
    recordDateTime ASC

END

GO

