CREATE TABLE [v9].[BmsReadingMinute] (
    [id]                BIGINT             IDENTITY (1, 1) NOT NULL,
    [deviceId]          INT                NOT NULL,
    [periodStart]       DATETIME2          NOT NULL,
    [avgPackVoltageMv]  BIGINT             NOT NULL,
    [avgCurrentMa]      INT                NOT NULL,
    [maxCurrentMa]      INT                NOT NULL,
    [minCurrentMa]      INT                NOT NULL,
    [avgPowerWatts]     FLOAT              NOT NULL,
    [maxPowerWatts]     FLOAT              NOT NULL,
    [avgSocPercent]     FLOAT              NOT NULL,
    [minSocPercent]     FLOAT              NOT NULL,
    [maxBatteryTemp1C]  FLOAT              NOT NULL,
    [maxBatteryTemp2C]  FLOAT              NOT NULL,
    [maxPowerTubeC]     FLOAT              NOT NULL,
    [maxDeltaCellMv]    INT                NOT NULL,
    [alarmCount]        INT                NOT NULL,
    CONSTRAINT [PK_BmsReadingMinute] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsReadingMinute_BmsDevice] FOREIGN KEY ([deviceId]) REFERENCES [v9].[BmsDevice] ([id]) ON DELETE CASCADE
);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsReadingMinute_DeviceId_PeriodStart]
    ON [v9].[BmsReadingMinute] ([deviceId] ASC, [periodStart] ASC);

GO
