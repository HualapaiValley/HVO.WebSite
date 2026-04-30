CREATE TABLE [v9].[BmsReading] (
    [id]                  BIGINT             IDENTITY (1, 1) NOT NULL,
    [deviceId]            INT                NOT NULL,
    [recordedAt]          DATETIMEOFFSET (7) NOT NULL,
    [packVoltageMv]       BIGINT             NOT NULL,
    [currentMa]           INT                NOT NULL,
    [powerWatts]          DECIMAL (9, 3)     NOT NULL,
    [socPercent]          TINYINT            NOT NULL,
    [sohPercent]          TINYINT            NOT NULL,
    [remainingCapacityMah] BIGINT            NOT NULL,
    [nominalCapacityMah]  BIGINT             NOT NULL,
    [cycleCount]          BIGINT             NOT NULL,
    [cycleCapacityMah]    BIGINT             NOT NULL,
    [batteryTemp1C]       DECIMAL (9, 2)     NOT NULL,
    [batteryTemp2C]       DECIMAL (9, 2)     NOT NULL,
    [powerTubeC]          DECIMAL (9, 2)     NOT NULL,
    [balancingActive]     BIT                NOT NULL,
    [balancingCurrentMa]  DECIMAL (9, 2)     NOT NULL,
    [deltaCellVoltageMv]  INT                NOT NULL,
    [alarmBitmask]        BIGINT             NOT NULL,
    CONSTRAINT [PK_BmsReading] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsReading_BmsDevice] FOREIGN KEY ([deviceId]) REFERENCES [v9].[BmsDevice] ([id]) ON DELETE CASCADE
);

GO

CREATE NONCLUSTERED INDEX [IX_BmsReading_RecordedAt]
    ON [v9].[BmsReading] ([recordedAt] ASC);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsReading_DeviceId_RecordedAt]
    ON [v9].[BmsReading] ([deviceId] ASC, [recordedAt] ASC);

GO
