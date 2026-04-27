CREATE TABLE [dbo].[OutbackMateFlexNetRecords_NEW] (
    [id]                   INT                IDENTITY (1, 1) NOT NULL,
    [recordDateTime]       DATETIMEOFFSET (7) NOT NULL,
    [hubPort]              TINYINT            NOT NULL,
    [shuntAEnabled]        BIT                NOT NULL,
    [shuntAAmps]           DECIMAL (9, 2)     NOT NULL,
    [shuntBEnabled]        BIT                NOT NULL,
    [shuntBAmps]           DECIMAL (9, 2)     NOT NULL,
    [shuntCEnabled]        BIT                NOT NULL,
    [shuntCAmps]           DECIMAL (9, 2)     NOT NULL,
    [batteryVoltage]       DECIMAL (9, 2)     NOT NULL,
    [batteryStateOfCharge] TINYINT            NOT NULL,
    [batteryTemperatureC]  SMALLINT           NULL,
    [extraValueTypeId]     TINYINT            NULL,
    [extraValue]           DECIMAL (9, 2)     NULL,
    [chargeParamsMet]      BIT                NOT NULL,
    [relayState]           TINYINT            NOT NULL,
    [relayMode]            TINYINT            NOT NULL,
    CONSTRAINT [PK_OutbackMateFlexNetRecords] PRIMARY KEY NONCLUSTERED ([id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_OutbackMateFlexNetRecords_cover01]
    ON [dbo].[OutbackMateFlexNetRecords_NEW]([recordDateTime] ASC)
    INCLUDE([shuntAAmps], [shuntBAmps], [shuntCAmps], [batteryVoltage], [batteryStateOfCharge], [batteryTemperatureC], [chargeParamsMet], [shuntAEnabled], [shuntBEnabled], [shuntCEnabled]);


GO

