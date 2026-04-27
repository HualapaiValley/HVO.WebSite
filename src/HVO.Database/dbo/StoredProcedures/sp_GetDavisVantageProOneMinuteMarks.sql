



CREATE PROCEDURE [dbo].[sp_GetDavisVantageProOneMinuteMarks] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN
  SELECT 
    MIN(DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))) as recordDateTime,
    MAX(recordDateTime) as recordDateTimeMax
  FROM 
    [dbo].[DavisVantageProConsoleRecords_NEW]
  WHERE
    recordDateTime BETWEEN @iRecordDateTimeStart AND @iRecordDateTimeEnd 
  GROUP BY
    DATEPART(YEAR, recordDateTime), DATEPART(MONTH, recordDateTime), DATEPART(DAY, recordDateTime), DATEPART(HOUR, recordDateTime), DATEPART(MINUTE, recordDateTime)
  ORDER BY
    recordDateTime 

END

GO

