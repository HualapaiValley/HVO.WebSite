
CREATE PROCEDURE [dbo].[sp_GetDavisVantageProConsoleRecordHighLow] 
  @iStartRecordDateTime datetimeoffset(7),
  @iEndRecordDateTime datetimeoffset(7)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from interfering with SELECT statements.
  SET NOCOUNT ON;
  
  WITH BarometerHigh AS (
    SELECT TOP 1
      barometer as barometerHigh,
      recordDateTime as barometerHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime)
    ORDER BY
      barometer DESC, recordDateTime DESC
  ),  
    
  BarometerLow AS (
    SELECT TOP 1
      barometer as barometerLow,
      recordDateTime as barometerLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime)
    ORDER BY
      barometer ASC, recordDateTime DESC
  ), 
        
  InsideTemperatureHigh AS (
    SELECT TOP 1
      insideTemperature as insideTemperatureHigh,
      recordDateTime as insideTemperatureHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      insideTemperature is not null
    ORDER BY
      insideTemperature DESC, recordDateTime DESC
  ),  
       
  InsideTemperatureLow AS (
    SELECT TOP 1
      insideTemperature as insideTemperatureLow,
      recordDateTime as insideTemperatureLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      insideTemperature is not null
    ORDER BY
      insideTemperature ASC, recordDateTime DESC
  ),   
      
  InsideHumidityHigh AS (
    SELECT TOP 1
      insideHumidity as insideHumidityHigh,
      recordDateTime as insideHumidityHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      insideHumidity is not null
    ORDER BY
      insideHumidity DESC, recordDateTime DESC
  ),
         
  InsideHumidityLow AS (
    SELECT TOP 1
      insideHumidity as insideHumidityLow,
      recordDateTime as insideHumidityLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      insideHumidity is not null
    ORDER BY
      insideHumidity ASC, recordDateTime DESC
  ),
  
  OutsideTemperatureHigh AS (
    SELECT TOP 1
      outsideTemperature as outsideTemperatureHigh,
      recordDateTime as outsideTemperatureHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideTemperature is not null
    ORDER BY
      outsideTemperature DESC, recordDateTime DESC
  ),  
       
  OutsideTemperatureLow AS (
    SELECT TOP 1
      outsideTemperature as outsideTemperatureLow,
      recordDateTime as outsideTemperatureLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideTemperature is not null
    ORDER BY
      outsideTemperature ASC, recordDateTime DESC
  ), 
        
  OutsideHumidityHigh AS (
    SELECT TOP 1
      outsideHumidity as outsideHumidityHigh,
      recordDateTime as outsideHumidityHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideHumidity is not null
    ORDER BY
      outsideHumidity DESC, recordDateTime DESC
  ), 
        
  OutsideHumidityLow AS (
    SELECT TOP 1
      outsideHumidity as outsideHumidityLow,
      recordDateTime as outsideHumidityLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideHumidity is not null
    ORDER BY
      outsideHumidity ASC, recordDateTime DESC
  ),
  
  WindSpeedHigh AS (
    SELECT TOP 1
      windSpeed as windSpeedHigh,
      windDirection as windSpeedHighDirection,
      recordDateTime as windSpeedHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime)
    ORDER BY
      windSpeed DESC, recordDateTime DESC
  ),
  
  WindSpeedLow AS (
    SELECT TOP 1
      windSpeed as windSpeedLow,
      windDirection as windSpeedLowDirection,
      recordDateTime as windSpeedLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime)
    ORDER BY
      windSpeed ASC, recordDateTime DESC
  ),
  
  SolarRadiationHigh AS (
    SELECT TOP 1
      solarRadiation as solarRadiationHigh,
      recordDateTime as solarRadiationHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime)
    ORDER BY
      solarRadiation DESC, recordDateTime DESC
  ),
  
  UVIndexHigh AS (
    SELECT TOP 1
      uvIndex as uvIndexHigh,
      recordDateTime as uvIndexHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime)
    ORDER BY
      uvIndex DESC, recordDateTime DESC
  ),
  
  UVIndexLow AS (
    SELECT TOP 1
      uvIndex as uvIndexLow,
      recordDateTime as uvIndexLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime)
    ORDER BY
      uvIndex ASC, recordDateTime DESC
  ),
  
  OutsideHeatIndexHigh AS (
    SELECT TOP 1
      outsideHeatIndex as outsideHeatIndexHigh,
      recordDateTime as outsideHeatIndexHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideHeatIndex is not null
    ORDER BY
      outsideHeatIndex DESC, recordDateTime DESC
  ),  
    
  OutsideHeatIndexLow AS (
    SELECT TOP 1
      outsideHeatIndex as outsideHeatIndexLow,
      recordDateTime as outsideHeatIndexLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideHeatIndex is not null
    ORDER BY
      outsideHeatIndex ASC, recordDateTime DESC
  ),
  
  OutsideWindChillHigh AS (
    SELECT TOP 1
      outsideWindChill as outsideWindChillHigh,
      recordDateTime as outsideWindChillHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideWindChill is not null
    ORDER BY
      outsideWindChill DESC, recordDateTime DESC
  ),   
   
  OutsideWindChillLow AS (
    SELECT TOP 1
      outsideWindChill as outsideWindChillLow,
      recordDateTime as outsideWindChillLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideWindChill is not null
    ORDER BY
      outsideWindChill ASC, recordDateTime DESC
  ),
  
  OutsideDewpointHigh AS (
    SELECT TOP 1
      outsideDewpoint as outsideDewpointHigh,
      recordDateTime as outsideDewpointHighDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND
      outsideDewpoint is not null
    ORDER BY
      outsideDewpoint DESC, recordDateTime DESC
  ), 
     
  OutsideDewpointLow AS (
    SELECT TOP 1
      outsideDewpoint as outsideDewpointLow,
      recordDateTime as outsideDewpointLowDateTime
    FROM
      [DavisVantageProConsoleRecords_NEW]
    WHERE
      (recordDateTime >= @iStartRecordDateTime) and (recordDateTime <= @iEndRecordDateTime) AND 
      outsideDewpoint is not null
    ORDER BY
      outsideDewpoint ASC, recordDateTime DESC
  )       

  SELECT 
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
    UVIndexLow.*,
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
    CROSS JOIN UVIndexLow
    CROSS JOIN OutsideHeatIndexHigh
    CROSS JOIN OutsideHeatIndexLow
    CROSS JOIN OutsideWindChillHigh
    CROSS JOIN OutsideWindChillLow
    CROSS JOIN OutsideDewpointHigh
    CROSS JOIN OutsideDewpointLow

END

GO

