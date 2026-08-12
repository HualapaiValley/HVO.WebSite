CREATE TABLE [v9].[SmartShuntDetailSnapshot] (
    [Id]               BIGINT        IDENTITY (1, 1) NOT NULL,
    [SourceId]         NVARCHAR (64) NOT NULL,
    [SourceSystem]     NVARCHAR (64) NULL,
    [DeviceId]         NVARCHAR (64) NULL,
    [RecordedAt]       DATETIME2     NOT NULL,
    [ConsumedAh]       FLOAT         NULL,
    [RemainingMinutes] FLOAT         NULL,
    [StarterVoltageV]  FLOAT         NULL,
    [TemperatureC]     FLOAT         NULL,
    [CreatedAt]        DATETIME2     NOT NULL,
    CONSTRAINT [PK_SmartShuntDetailSnapshot] PRIMARY KEY CLUSTERED ([Id] ASC)
);

GO

CREATE NONCLUSTERED INDEX [IX_SmartShuntDetailSnapshot_RecordedAt]
    ON [v9].[SmartShuntDetailSnapshot] ([RecordedAt] ASC);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_SmartShuntDetailSnapshot_SourceId_RecordedAt]
    ON [v9].[SmartShuntDetailSnapshot] ([SourceId] ASC, [RecordedAt] ASC);

GO
