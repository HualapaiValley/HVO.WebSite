
CREATE PROCEDURE [dbo].[sp__GetWeatherRecordHighLowSummary2] 
  @iStartRecordDateTime datetimeoffset,
  @iEndRecordDateTime datetimeoffset
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from interfering with SELECT statements.
  SET NOCOUNT ON;

--  SELECT
--    RecordDateTime
--    ,Barometer
--	,insideTemperature
--	,insideHumidity
--	,outsideTemperature
--	,outsideHumidity
--	,windSpeed
--	,windDirection
--	,solarRadiation
--	,uvIndex
--	,outsideHeatIndex
--	,outsideWindChill
--	,outsideDewpoint
--  INTO 
--    #TempRecords
--  FROM 
--    [dbo].[DavisVantageProConsoleRecords_NEW]
--  WHERE
--    (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime)
----  ORDER BY
----    recordDateTime DESC

BEGIN  
  WITH BarometerHigh AS (
    SELECT TOP 1
      barometer as BarometerHigh,
      recordDateTime as BarometerHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime)
    ORDER BY
      barometer DESC, recordDateTime DESC
  ),  
    
  BarometerLow AS (
    SELECT TOP 1
      barometer as BarometerLow,
      recordDateTime as BarometerLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime)
    ORDER BY
      barometer ASC, recordDateTime DESC
  ), 
        
  InsideTemperatureHigh AS (
    SELECT TOP 1
      insideTemperature as InsideTemperatureHigh,
      recordDateTime as InsideTemperatureHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      insideTemperature is not null
    ORDER BY
      insideTemperature DESC, recordDateTime DESC
  ),  
       
  InsideTemperatureLow AS (
    SELECT TOP 1
      insideTemperature as InsideTemperatureLow,
      recordDateTime as InsideTemperatureLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      insideTemperature is not null
    ORDER BY
      insideTemperature ASC, recordDateTime DESC
  ),   
      
  InsideHumidityHigh AS (
    SELECT TOP 1
      insideHumidity as InsideHumidityHigh,
      recordDateTime as InsideHumidityHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      insideHumidity is not null
    ORDER BY
      insideHumidity DESC, recordDateTime DESC
  ),
         
  InsideHumidityLow AS (
    SELECT TOP 1
      insideHumidity as InsideHumidityLow,
      recordDateTime as InsideHumidityLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      insideHumidity is not null
    ORDER BY
      insideHumidity ASC, recordDateTime DESC
  ),
  
  OutsideTemperatureHigh AS (
    SELECT TOP 1
      outsideTemperature as OutsideTemperatureHigh,
      recordDateTime as OutsideTemperatureHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideTemperature is not null
    ORDER BY
      outsideTemperature DESC, recordDateTime DESC
  ),  
       
  OutsideTemperatureLow AS (
    SELECT TOP 1
      outsideTemperature as OutsideTemperatureLow,
      recordDateTime as OutsideTemperatureLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideTemperature is not null
    ORDER BY
      outsideTemperature ASC, recordDateTime DESC
  ), 
        
  OutsideHumidityHigh AS (
    SELECT TOP 1
      outsideHumidity as OutsideHumidityHigh,
      recordDateTime as OutsideHumidityHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideHumidity is not null
    ORDER BY
      outsideHumidity DESC, recordDateTime DESC
  ), 
        
  OutsideHumidityLow AS (
    SELECT TOP 1
      outsideHumidity as OutsideHumidityLow,
      recordDateTime as OutsideHumidityLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideHumidity is not null
    ORDER BY
      outsideHumidity ASC, recordDateTime DESC
  ),
  
  WindSpeedHigh AS (
    SELECT TOP 1
      windSpeed as WindSpeedHigh,
      windDirection as WindSpeedHighDirection,
      recordDateTime as WindSpeedHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime)
    ORDER BY
      windSpeed DESC, recordDateTime DESC
  ),
  
  WindSpeedLow AS (
    SELECT TOP 1
      windSpeed as WindSpeedLow,
      windDirection as WindSpeedLowDirection,
      recordDateTime as WindSpeedLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) 
    ORDER BY
      windSpeed ASC, recordDateTime DESC
  ),
  
  SolarRadiationHigh AS (
    SELECT TOP 1
      solarRadiation as SolarRadiationHigh,
      recordDateTime as SolarRadiationHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) 
    ORDER BY
      solarRadiation DESC, recordDateTime DESC
  ),
  
  UVIndexHigh AS (
    SELECT TOP 1
      uvIndex as UVIndexHigh,
      recordDateTime as UVIndexHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime)
    ORDER BY
      uvIndex DESC, recordDateTime DESC
  ),
  
  OutsideHeatIndexHigh AS (
    SELECT TOP 1
      outsideHeatIndex as OutsideHeatIndexHigh,
      recordDateTime as OutsideHeatIndexHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideHeatIndex is not null
    ORDER BY
      outsideHeatIndex DESC, recordDateTime DESC
  ),  
    
  OutsideHeatIndexLow AS (
    SELECT TOP 1
      outsideHeatIndex as OutsideHeatIndexLow,
      recordDateTime as OutsideHeatIndexLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideHeatIndex is not null
    ORDER BY
      outsideHeatIndex ASC, recordDateTime DESC
  ),
  
  OutsideWindChillHigh AS (
    SELECT TOP 1
      outsideWindChill as OutsideWindChillHigh,
      recordDateTime as OutsideWindChillHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideWindChill is not null
    ORDER BY
      outsideWindChill DESC, recordDateTime DESC
  ),   
   
  OutsideWindChillLow AS (
    SELECT TOP 1
      outsideWindChill as OutsideWindChillLow,
      recordDateTime as OutsideWindChillLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideWindChill is not null
    ORDER BY
      outsideWindChill ASC, recordDateTime DESC
  ),
  
  OutsideDewpointHigh AS (
    SELECT TOP 1
      outsideDewpoint as OutsideDewpointHigh,
      recordDateTime as OutsideDewpointHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideDewpoint is not null
    ORDER BY
      outsideDewpoint DESC, recordDateTime DESC
  ), 
     
  OutsideDewpointLow AS (
    SELECT TOP 1
      outsideDewpoint as OutsideDewpointLow,
      recordDateTime as OutsideDewpointLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime BETWEEN @iStartRecordDateTime AND @iEndRecordDateTime) AND
      outsideDewpoint is not null
    ORDER BY
      outsideDewpoint ASC, recordDateTime DESC
  )       


  SELECT 
    @iStartRecordDateTime as StartRecordDateTime,
	@iEndRecordDateTime as EndRecordDateTime,
    
    BarometerHigh.*,
    BarometerLow.*,
    InsideTemperatureHigh.*,
    InsideTemperatureLow.*,
    InsideHumidityHigh.*,
    InsideHumidityLow.*,
    OutsideTemperatureHigh.*,
    OutsideTemperatureLow.*,
    OutsideHumidityHigh.*,
    OutsideHumidityLow.*,
    WindSpeedHigh.*,
    WindSpeedLow.*,
    SolarRadiationHigh.*,
    UVIndexHigh.*,
    OutsideHeatIndexHigh.*,
    OutsideHeatIndexLow.*,
    OutsideWindChillHigh.*,
    OutsideWindChillLow.*,
    OutsideDewpointHigh.*,
    OutsideDewpointLow.*
  FROM 
                                                                        
    BarometerHigh
    CROSS JOIN BarometerLow
    CROSS JOIN InsideTemperatureHigh
    CROSS JOIN InsideTemperatureLow
    CROSS JOIN InsideHumidityHigh
    CROSS JOIN InsideHumidityLow
    CROSS JOIN OutsideTemperatureHigh
    CROSS JOIN OutsideTemperatureLow
    CROSS JOIN OutsideHumidityHigh
    CROSS JOIN OutsideHumidityLow
    CROSS JOIN WindSpeedHigh
    CROSS JOIN WindSpeedLow
    CROSS JOIN SolarRadiationHigh
    CROSS JOIN UVIndexHigh
    CROSS JOIN OutsideHeatIndexHigh
    CROSS JOIN OutsideHeatIndexLow
    CROSS JOIN OutsideWindChillHigh
    CROSS JOIN OutsideWindChillLow
    CROSS JOIN OutsideDewpointHigh
    CROSS JOIN OutsideDewpointLow

END

END

GO

