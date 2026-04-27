


CREATE PROCEDURE [dbo].[OutbackMateFlexNetOneMinuteAverage_Rollup] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN
  SELECT 
    MIN(DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    MAX(hubPort) as hubPort,
    AVG(shuntAAmps) as shuntAAmps,
    AVG(shuntBAmps) as shuntBAmps,
    AVG(shuntCAmps) as shuntCAmps,

    MAX(CONVERT(int,shuntAEnabled)) as shuntAEnabled,
    MAX(CONVERT(int,shuntBEnabled)) as shuntBEnabled,
    MAX(CONVERT(int,shuntBEnabled)) as shuntCEnabled,

    AVG(batteryVoltage) as batteryVoltage,
    AVG(batteryStateOfCharge) as batteryStateOfCharge,
    AVG(batteryTemperatureC) as batteryTemperatureC,
    
    MAX(extraValueTypeId) as extraValueTypeId,
    MAX(extraValue) as extraValue,
    
    MAX(CONVERT(int,chargeParamsMet)) as chargeParamsMet,
    MAX(relayState) as relayState,
    MAX(relayMode) as relayMode
  INTO
    #Temp_OutbackMateFlexNetRecords_Rollup
  FROM 
    [dbo].[OutbackMateFlexNetRecords_NEW]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime), DATEPART(minute, recordDateTime)
  ORDER BY
    recordDateTime 


INSERT INTO OutbackMateFlexNetRecords_OneMinuteArchive
 (recordDateTime,
  hubPort,
  shuntAEnabled, 
  shuntAAmps,
  shuntBEnabled,
  shuntBAmps,
  shuntCEnabled,
  shuntCAmps,
  batteryVoltage,
  batteryStateOfCharge,
  batteryTemperatureC,
  extraValueTypeId,
  extraValue,
  chargeParamsMet,
  relayState,
  relayMode)
SELECT
  recordDateTime,
  hubPort,
  CONVERT(bit, shuntAEnabled), 
  shuntAAmps,
  CONVERT(bit, shuntBEnabled), 
  shuntBAmps,
  CONVERT(bit, shuntCEnabled), 
  shuntCAmps,
  batteryVoltage,
  batteryStateOfCharge,
  batteryTemperatureC,
  extraValueTypeId,
  extraValue,
  CONVERT(bit, chargeParamsMet),
  relayState,
  relayMode
FROM
  #Temp_OutbackMateFlexNetRecords_Rollup
  
  
  --SELECT * FROM #Temp_OutbackMateFlexNetRecords_Rollup

END

GO

