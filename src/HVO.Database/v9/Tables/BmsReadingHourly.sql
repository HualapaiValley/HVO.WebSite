CREATE TABLE [v9].[BmsReadingHourly] (
    [id]                BIGINT             IDENTITY (1, 1) NOT NULL,
    [deviceId]          INT                NOT NULL,
    [periodStart]       DATETIMEOFFSET (7) NOT NULL,
    [avgPackVoltageMv]  BIGINT             NOT NULL,
    [avgCurrentMa]      INT                NOT NULL,
    [maxCurrentMa]      INT                NOT NULL,
    [minCurrentMa]      INT                NOT NULL,
    [avgPowerWatts]     DECIMAL (9, 3)     NOT NULL,
    [maxPowerWatts]     DECIMAL (9, 3)     NOT NULL,
    [avgSocPercent]     DECIMAL (5, 2)     NOT NULL,
    [minSocPercent]     DECIMAL (5, 2)     NOT NULL,
    [maxBatteryTemp1C]  DECIMAL (9, 2)     NOT NULL,
    [maxBatteryTemp2C]  DECIMAL (9, 2)     NOT NULL,
    [maxPowerTubeC]     DECIMAL (9, 2)     NOT NULL,
    [maxDeltaCellMv]    INT                NOT NULL,
    [alarmCount]        INT                NOT NULL,
    CONSTRAINT [PK_BmsReadingHourly] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsReadingHourly_BmsDevice] FOREIGN KEY ([deviceId]) REFERENCES [v9].[BmsDevice] ([id]) ON DELETE CASCADE
);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsReadingHourly_DeviceId_PeriodStart]
    ON [v9].[BmsReadingHourly] ([deviceId] ASC, [periodStart] ASC);

GO
