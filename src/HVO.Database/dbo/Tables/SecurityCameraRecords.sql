CREATE TABLE [dbo].[SecurityCameraRecords] (
    [Id]              INT                IDENTITY (1, 1) NOT NULL,
    [RecordDateTime]  DATETIMEOFFSET (7) NOT NULL,
    [ImageType]       TINYINT            NOT NULL,
    [CameraNumber]    TINYINT            NOT NULL,
    [StorageLocation] VARCHAR (MAX)      NOT NULL,
    CONSTRAINT [PK_SecurityCameraRecords] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_SecurityCameraRecords]
    ON [dbo].[SecurityCameraRecords]([RecordDateTime] ASC, [CameraNumber] ASC, [ImageType] ASC);


GO

