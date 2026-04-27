CREATE TABLE [dbo].[WeatherSatelliteRecords] (
    [Id]              INT                IDENTITY (1, 1) NOT NULL,
    [RecordDateTime]  DATETIMEOFFSET (7) NOT NULL,
    [ImageType]       TINYINT            NOT NULL,
    [CameraNumber]    TINYINT            NOT NULL,
    [StorageLocation] VARCHAR (MAX)      NOT NULL,
    CONSTRAINT [PK_WeatherSatelliteRecords] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_WeatherSatelliteRecords]
    ON [dbo].[WeatherSatelliteRecords]([RecordDateTime] ASC, [ImageType] ASC);


GO

