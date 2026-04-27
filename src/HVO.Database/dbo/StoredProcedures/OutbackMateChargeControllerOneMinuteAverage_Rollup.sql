

CREATE PROCEDURE [dbo].[OutbackMateChargeControllerOneMinuteAverage_Rollup] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN

  SELECT 
    MIN(DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    MAX(hubPort) as hubPort,
    CAST(AVG(pvAmps) as tinyint) as pvAmps,
    CAST(AVG(pvVoltage) as tinyint) as pvVoltage,
    CAST(AVG(chargerAmps) as decimal(9, 2)) as chargerAmps,
    CAST(AVG(chargerVoltage) as decimal(9, 2)) as chargerVoltage,
    MAX(dailyAmpHoursProduced) as dailyAmpHoursProduced,
    MAX(dailyWattHoursProduced) as dailyWattHoursProduced,
    MAX(chargerMode) as chargerMode,
    MAX(chargerAuxRelayMode) as chargerAuxRelayMode,
    MAX(chargerErrorMode) as chargerErrorMode
  INTO
    #Temp_OutbackMateChargeControllerOneMinuteAverage_Rollup
  FROM 
    [dbo].[OutbackMateChargeControllerRecords_NEW]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime), DATEPART(minute, recordDateTime)
  ORDER BY
    recordDateTime
     

  INSERT INTO OutbackMateChargeControllerRecords_OneMinuteArchive (
    recordDateTime, 
    hubPort, 
    pvAmps, 
    pvVoltage, 
    chargerAmps, 
    chargerVoltage, 
    dailyAmpHoursProduced, 
    dailyWattHoursProduced, 
    chargerMode, 
    chargerAuxRelayMode, 
    chargerErrorMode)
  SELECT
    recordDateTime, 
    hubPort, 
    pvAmps, 
    pvVoltage, 
    chargerAmps, 
    chargerVoltage, 
    dailyAmpHoursProduced, 
    dailyWattHoursProduced, 
    chargerMode, 
    chargerAuxRelayMode, 
    chargerErrorMode
  FROM 
    #Temp_OutbackMateChargeControllerOneMinuteAverage_Rollup
  
  SELECT COUNT(*) FROM #Temp_OutbackMateChargeControllerOneMinuteAverage_Rollup

END

GO

