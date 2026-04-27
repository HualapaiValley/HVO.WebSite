CREATE TABLE [dbo].[WeatherCameraRecords] (
    [Id]              INT                IDENTITY (1, 1) NOT NULL,
    [RecordDateTime]  DATETIMEOFFSET (7) NOT NULL,
    [ImageType]       TINYINT            NOT NULL,
    [CameraNumber]    TINYINT            NOT NULL,
    [StorageLocation] VARCHAR (MAX)      NOT NULL,
    CONSTRAINT [PK_WeatherCameraRecords] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_WeatherCameraRecords]
    ON [dbo].[WeatherCameraRecords]([RecordDateTime] ASC, [CameraNumber] ASC, [ImageType] ASC);


GO

