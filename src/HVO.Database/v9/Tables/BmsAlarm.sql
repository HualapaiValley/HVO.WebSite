CREATE TABLE [v9].[BmsAlarm] (
    [id]            BIGINT             IDENTITY (1, 1) NOT NULL,
    [deviceId]      INT                NOT NULL,
    [alarmBitmask]  BIGINT             NOT NULL,
    [activatedAt]   DATETIME2          NOT NULL,
    [clearedAt]     DATETIME2          NULL,
    CONSTRAINT [PK_BmsAlarm] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsAlarm_BmsDevice] FOREIGN KEY ([deviceId]) REFERENCES [v9].[BmsDevice] ([id]) ON DELETE CASCADE
);

GO

CREATE NONCLUSTERED INDEX [IX_BmsAlarm_DeviceId_ClearedAt]
    ON [v9].[BmsAlarm] ([deviceId] ASC, [clearedAt] ASC);

GO
