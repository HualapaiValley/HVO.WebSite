
CREATE PROCEDURE [dbo].[sp_GetOutbackMateFlexNetOneMinuteAverage] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT                        
    MIN(DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,

    CAST(AVG(shuntAAmps) as decimal(9, 2)) as shuntAAmps,
    CAST(AVG(shuntAAmps * batteryVoltage) as decimal(9, 2)) as shuntAWatts,
    CAST(MAX(CASE(shuntAEnabled) WHEN 0 THEN 0 ELSE 1 END) as bit) as shuntAEnabled,

    CAST(AVG(shuntBAmps) as decimal(9, 2)) as shuntBAmps,
    CAST(AVG(shuntBAmps * batteryVoltage) as decimal(9, 2)) as shuntBWatts,
    CAST(MAX(CASE(shuntBEnabled) WHEN 0 THEN 0 ELSE 1 END) as bit) as shuntBEnabled,

    CAST(AVG(shuntCAmps) as decimal(9, 2)) as shuntCAmps,
    CAST(AVG(shuntCAmps * batteryVoltage) as decimal(9, 2)) as shuntCWatts,
    CAST(MAX(CASE(shuntCEnabled) WHEN 0 THEN 0 ELSE 1 END) as bit) as shuntCEnabled,

    CAST(AVG(batteryVoltage) as decimal(9, 2)) as batteryVoltage,
	AVG(batteryStateOfCharge) as batteryStateOfCharge,

    CAST(AVG(batteryTemperatureC) as decimal(9, 2)) as batteryTemperatureC,
    CAST(MAX(CASE(chargeParamsMet) WHEN 0 THEN 0 ELSE 1 END) as bit) as chargeParamsMet
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

