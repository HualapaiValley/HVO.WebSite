CREATE TABLE [dbo].[WebPowerSwitchConfigurations] (
    [Id]           INT          IDENTITY (1, 1) NOT NULL,
    [Name]         VARCHAR (50) NOT NULL,
    [SerialNumber] VARCHAR (10) NOT NULL,
    [Address]      VARCHAR (50) NOT NULL,
    [Username]     VARCHAR (25) NOT NULL,
    [Password]     VARCHAR (25) NOT NULL,
    [Enabled]      BIT          DEFAULT ((0)) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_WebPowerSwitchConfiguration_SerialNumber]
    ON [dbo].[WebPowerSwitchConfigurations]([SerialNumber] ASC);


GO

