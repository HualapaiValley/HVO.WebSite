CREATE TABLE [v9].[BmsDevice] (
    [id]          INT                IDENTITY (1, 1) NOT NULL,
    [siteId]      INT                NULL,
    [address]     NVARCHAR (17)      NOT NULL,
    [alias]       NVARCHAR (100)     NOT NULL,
    [firstSeenAt] DATETIME2          NOT NULL,
    [notes]       NVARCHAR (500)     NULL,
    CONSTRAINT [PK_BmsDevice] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsDevice_BmsSite] FOREIGN KEY ([siteId]) REFERENCES [v9].[BmsSite] ([id]) ON DELETE SET NULL
);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsDevice_Address]
    ON [v9].[BmsDevice] ([address] ASC);

GO
