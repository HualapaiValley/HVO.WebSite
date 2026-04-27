CREATE TABLE [dbo].[DavisVantageProConsoleRecords_NEW] (
    [id]                        INT                IDENTITY (1, 1) NOT NULL,
    [recordDateTime]            DATETIMEOFFSET (7) NOT NULL,
    [barometer]                 DECIMAL (9, 2)     NOT NULL,
    [barometerTrend]            SMALLINT           NOT NULL,
    [insideTemperature]         DECIMAL (9, 2)     NOT NULL,
    [insideHumidity]            TINYINT            NOT NULL,
    [outsideTemperature]        DECIMAL (9, 2)     NULL,
    [outsideHumidity]           TINYINT            NULL,
    [windSpeed]                 TINYINT            NULL,
    [windDirection]             SMALLINT           NULL,
    [tenMinuteWindSpeedAverage] TINYINT            NULL,
    [rainRate]                  DECIMAL (9, 2)     NULL,
    [uvIndex]                   TINYINT            NULL,
    [solarRadiation]            SMALLINT           NULL,
    [stormRain]                 DECIMAL (9, 2)     NULL,
    [stormStartDate]            DATETIMEOFFSET (7) NULL,
    [dailyRainAmount]           DECIMAL (9, 2)     NULL,
    [monthlyRainAmount]         DECIMAL (9, 2)     NULL,
    [yearlyRainAmount]          DECIMAL (9, 2)     NULL,
    [consoleBatteryVoltage]     DECIMAL (9, 2)     NULL,
    [forcastIcons]              SMALLINT           NULL,
    [sunriseTime]               TIME (7)           NULL,
    [sunsetTime]                TIME (7)           NULL,
    [dailyETAmount]             DECIMAL (9, 2)     NULL,
    [monthlyETAmount]           DECIMAL (9, 2)     NULL,
    [yearlyETAmount]            DECIMAL (9, 2)     NULL,
    [outsideHeatIndex]          DECIMAL (9, 2)     NULL,
    [outsideWindChill]          DECIMAL (9, 2)     NULL,
    [outsideDewpoint]           DECIMAL (9, 2)     NULL,
    CONSTRAINT [PK_DavisVantageProConsoleRecords_NEW] PRIMARY KEY CLUSTERED ([id] ASC)
);


GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_DavisVantageProConsoleRecords_NEW]
    ON [dbo].[DavisVantageProConsoleRecords_NEW]([recordDateTime] ASC);


GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_DavisVantageProConsoleRecords_NEW_Cover]
    ON [dbo].[DavisVantageProConsoleRecords_NEW]([recordDateTime] ASC)
    INCLUDE([barometer], [insideTemperature], [insideHumidity], [outsideTemperature], [outsideHumidity], [windSpeed], [windDirection], [rainRate], [uvIndex], [solarRadiation], [stormRain], [stormStartDate], [dailyRainAmount], [monthlyRainAmount], [yearlyRainAmount], [consoleBatteryVoltage], [sunriseTime], [sunsetTime], [dailyETAmount], [monthlyETAmount], [yearlyETAmount], [outsideHeatIndex], [outsideWindChill], [outsideDewpoint], [id], [barometerTrend], [tenMinuteWindSpeedAverage], [forcastIcons]);


GO

