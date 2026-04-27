CREATE TABLE [dbo].[SkyMonitor] (
    [Id]                 INT                IDENTITY (1, 1) NOT NULL,
    [RecordDateTime]     DATETIMEOFFSET (7) NOT NULL,
    [DeviceId]           UNIQUEIDENTIFIER   NOT NULL,
    [IR]                 DECIMAL (9, 4)     NOT NULL,
    [Visible]            DECIMAL (9, 4)     NOT NULL,
    [Lux]                DECIMAL (9, 4)     NOT NULL,
    [Gain]               VARCHAR (10)       NOT NULL,
    [AmbientTemperature] DECIMAL (9, 2)     NOT NULL,
    [SkyTemperature]     DECIMAL (9, 2)     NOT NULL,
    CONSTRAINT [PK_SkyMonitor] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_SkyMonitor_RecordDateTime]
    ON [dbo].[SkyMonitor]([RecordDateTime] ASC);


GO

