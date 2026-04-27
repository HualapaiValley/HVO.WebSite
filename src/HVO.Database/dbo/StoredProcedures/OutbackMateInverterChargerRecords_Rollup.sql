


CREATE PROCEDURE [dbo].[OutbackMateInverterChargerRecords_Rollup] 
  @iRecordDateTimeStart datetimeoffset(7),
  @iRecordDateTimeEnd datetimeoffset(7)
AS
BEGIN
  SELECT 
    MIN(DATEADD(SECOND, DATEPART(SECOND, recordDateTime) * -1, DATEADD(nanosecond, DATEPART(nanosecond, recordDateTime) * -1, recordDateTime))) as recordDateTime,
    MIN(recordDateTime) as recordDateTimeMin,
    MAX(recordDateTime) as recordDateTimeMax,
    MAX(hubPort) as hubPort,
    AVG(inverterCurrent) as inverterCurrent,
    AVG(chargerCurrent) as chargerCurrent,
    AVG(buyCurrent) as buyCurrent,
    AVG(ACInputVoltage) as ACInputVoltage,
    AVG(ACOutputVoltage) as ACOutputVoltage,
    AVG(SellCurrent) as SellCurrent,
    MAX(OperationalMode) as OperationalMode,
    MAX(ErrorMode) as ErrorMode,
    MAX(ACInputMode) as ACInputMode,
    AVG(BatteryVoltage) as BatteryVoltage,
    MAX(Misc) as Misc,
    MAX(WarningMode) as WarningMode
  INTO
    #Temp_OutbackMateInverterChargerRecords_Rollup
  FROM 
    [dbo].[OutbackMateInverterChargerRecords_NEW]
  WHERE
    (recordDateTime >= @iRecordDateTimeStart) AND 
    (recordDateTime <= @iRecordDateTimeEnd) 
  GROUP BY
    DATEPART(year, recordDateTime), DATEPART(month, recordDateTime), DATEPART(day, recordDateTime), DATEPART(hour, recordDateTime), DATEPART(minute, recordDateTime)
  ORDER BY
    recordDateTime 


INSERT INTO OutbackMateInverterChargerRecords_OneMinuteArchive
 ([RecordDateTime]
      ,[HubPort]
      ,[InverterCurrent]
      ,[ChargerCurrent]
      ,[BuyCurrent]
      ,[ACInputVoltage]
      ,[ACOutputVoltage]
      ,[SellCurrent]
      ,[OperationalMode]
      ,[ErrorMode]
      ,[ACInputMode]
      ,[BatteryVoltage]
      ,[Misc]
      ,[WarningMode])
SELECT
[RecordDateTime]
      ,[HubPort]
      ,[InverterCurrent]
      ,[ChargerCurrent]
      ,[BuyCurrent]
      ,[ACInputVoltage]
      ,[ACOutputVoltage]
      ,[SellCurrent]
      ,[OperationalMode]
      ,[ErrorMode]
      ,[ACInputMode]
      ,[BatteryVoltage]
      ,[Misc]
      ,[WarningMode]
    FROM
  #Temp_OutbackMateInverterChargerRecords_Rollup
  
  

END

GO

