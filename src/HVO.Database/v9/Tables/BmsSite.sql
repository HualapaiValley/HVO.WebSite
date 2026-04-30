CREATE TABLE [v9].[BmsSite] (
    [id]       INT            IDENTITY (1, 1) NOT NULL,
    [name]     NVARCHAR (100) NOT NULL,
    [location] NVARCHAR (200) NULL,
    [notes]    NVARCHAR (500) NULL,
    CONSTRAINT [PK_BmsSite] PRIMARY KEY CLUSTERED ([id] ASC)
);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsSite_Name]
    ON [v9].[BmsSite] ([name] ASC);

GO
