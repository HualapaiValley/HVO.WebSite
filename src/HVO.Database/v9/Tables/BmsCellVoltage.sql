CREATE TABLE [v9].[BmsCellVoltage] (
    [readingId]  BIGINT   NOT NULL,
    [cellIndex]  TINYINT  NOT NULL,
    [voltageMv]  INT      NOT NULL,
    CONSTRAINT [PK_BmsCellVoltage] PRIMARY KEY CLUSTERED ([readingId] ASC, [cellIndex] ASC),
    CONSTRAINT [FK_BmsCellVoltage_BmsReading] FOREIGN KEY ([readingId]) REFERENCES [v9].[BmsReading] ([id]) ON DELETE CASCADE
);

GO
