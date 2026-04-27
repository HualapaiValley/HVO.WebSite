CREATE TABLE [dbo].[OutbackMateInverterChargerRecords_NEW] (
    [id]              INT                IDENTITY (1, 1) NOT NULL,
    [recordDateTime]  DATETIMEOFFSET (7) NOT NULL,
    [hubPort]         TINYINT            NOT NULL,
    [inverterCurrent] TINYINT            NOT NULL,
    [chargerCurrent]  TINYINT            NOT NULL,
    [buyCurrent]      TINYINT            NOT NULL,
    [acInputVoltage]  SMALLINT           NOT NULL,
    [acOutputVoltage] SMALLINT           NOT NULL,
    [sellCurrent]     TINYINT            NOT NULL,
    [operationalMode] INT                NOT NULL,
    [errorMode]       INT                NOT NULL,
    [acInputMode]     INT                NOT NULL,
    [batteryVoltage]  DECIMAL (9, 2)     NOT NULL,
    [misc]            INT                NOT NULL,
    [warningMode]     INT                NOT NULL,
    CONSTRAINT [PK_OutbackMateInverterChargerRecords] PRIMARY KEY CLUSTERED ([id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_OutbackMateInverterChargerRecords_cover01]
    ON [dbo].[OutbackMateInverterChargerRecords_NEW]([recordDateTime] ASC)
    INCLUDE([hubPort], [inverterCurrent], [chargerCurrent], [acInputVoltage], [acOutputVoltage], [batteryVoltage], [errorMode], [operationalMode], [warningMode]);


GO

