CREATE TABLE [v9].[BmsCellResistance] (
    [readingId]       BIGINT   NOT NULL,
    [cellIndex]       TINYINT  NOT NULL,
    [resistanceMOhm]  INT      NOT NULL,
    CONSTRAINT [PK_BmsCellResistance] PRIMARY KEY CLUSTERED ([readingId] ASC, [cellIndex] ASC),
    CONSTRAINT [FK_BmsCellResistance_BmsReading] FOREIGN KEY ([readingId]) REFERENCES [v9].[BmsReading] ([id]) ON DELETE CASCADE
);

GO
