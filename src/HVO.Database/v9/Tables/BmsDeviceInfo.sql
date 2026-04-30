CREATE TABLE [v9].[BmsDeviceInfo] (
    [id]                 BIGINT             IDENTITY (1, 1) NOT NULL,
    [deviceId]           INT                NOT NULL,
    [recordedAt]         DATETIME2          NOT NULL,
    [manufacturer]       NVARCHAR (64)      NULL,
    [hardware]           NVARCHAR (32)      NULL,
    [firmware]           NVARCHAR (32)      NULL,
    [serialNumber]       NVARCHAR (64)      NULL,
    [deviceName]         NVARCHAR (64)      NULL,
    [manufacturingDate]  NVARCHAR (32)      NULL,
    [userData]           NVARCHAR (64)      NULL,
    CONSTRAINT [PK_BmsDeviceInfo] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsDeviceInfo_BmsDevice] FOREIGN KEY ([deviceId]) REFERENCES [v9].[BmsDevice] ([id]) ON DELETE CASCADE
);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsDeviceInfo_DeviceId_RecordedAt]
    ON [v9].[BmsDeviceInfo] ([deviceId] ASC, [recordedAt] ASC);

GO
