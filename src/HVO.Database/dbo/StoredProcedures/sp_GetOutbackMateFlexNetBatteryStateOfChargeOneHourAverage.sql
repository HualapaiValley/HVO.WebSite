



CREATE PROCEDURE [dbo].[sp_GetOutbackMateFlexNetBatteryStateOfChargeOneHourAverage] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT 
    MIN(DATEADD(MINUTE, DATEPART(MINUTE, recordDateTime) * -1, DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime)))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    MAX(batteryStateOfCharge) as batteryStateofCharge
  FROM 
    [dbo].[OutbackMateFlexNetRecords]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime)
  ORDER BY
    recordDateTime ASC

END

GO

