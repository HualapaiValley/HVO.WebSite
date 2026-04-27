CREATE TABLE [dbo].[CameraRecords] (
    [Id]              INT                IDENTITY (1, 1) NOT NULL,
    [RecordDateTime]  DATETIMEOFFSET (7) NOT NULL,
    [ImageType]       TINYINT            NOT NULL,
    [CameraNumber]    TINYINT            NOT NULL,
    [StorageLocation] VARCHAR (MAX)      NOT NULL,
    [CameraType]      TINYINT            NOT NULL,
    CONSTRAINT [PK_CameraRecords] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_CameraType_incIT_incCN]
    ON [dbo].[CameraRecords]([CameraType] ASC)
    INCLUDE([ImageType], [CameraNumber]);


GO

CREATE NONCLUSTERED INDEX [IX_CameraRecords_RDT_CN_IT_CT]
    ON [dbo].[CameraRecords]([RecordDateTime] DESC, [CameraNumber] ASC, [ImageType] ASC, [CameraType] ASC);


GO

CREATE NONCLUSTERED INDEX [IX_CameraRecords_ImageType_CameraNumber_RecordDateTime]
    ON [dbo].[CameraRecords]([ImageType] ASC, [CameraType] ASC)
    INCLUDE([RecordDateTime], [CameraNumber]);


GO

