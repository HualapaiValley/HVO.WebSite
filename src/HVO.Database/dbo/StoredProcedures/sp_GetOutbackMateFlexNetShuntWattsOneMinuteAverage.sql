



CREATE PROCEDURE [dbo].[sp_GetOutbackMateFlexNetShuntWattsOneMinuteAverage] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT 
    MIN(DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    AVG(shuntAAmps * batteryVoltage) as shuntAWatts,
    AVG(shuntBAmps * batteryVoltage) as shuntBWatts,
    AVG(shuntCAmps * batteryVoltage) as shuntCWatts
  FROM 
    [dbo].[OutbackMateFlexNetRecords]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime), DATEPART(minute, recordDateTime)
  ORDER BY
    recordDateTime ASC

END

GO

