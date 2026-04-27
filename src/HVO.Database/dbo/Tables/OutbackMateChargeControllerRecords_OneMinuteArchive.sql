CREATE TABLE [dbo].[OutbackMateChargeControllerRecords_OneMinuteArchive] (
    [id]                     INT                IDENTITY (1, 1) NOT NULL,
    [recordDateTime]         DATETIMEOFFSET (7) NOT NULL,
    [hubPort]                TINYINT            NOT NULL,
    [pvAmps]                 TINYINT            NOT NULL,
    [pvVoltage]              TINYINT            NOT NULL,
    [chargerAmps]            DECIMAL (9, 2)     NOT NULL,
    [chargerVoltage]         DECIMAL (9, 2)     NOT NULL,
    [dailyAmpHoursProduced]  SMALLINT           NOT NULL,
    [dailyWattHoursProduced] SMALLINT           NOT NULL,
    [chargerMode]            TINYINT            NOT NULL,
    [chargerAuxRelayMode]    TINYINT            NOT NULL,
    [chargerErrorMode]       TINYINT            NOT NULL,
    CONSTRAINT [PK_OutbackMateChargeControllerRecords_OneMinuteArchive] PRIMARY KEY CLUSTERED ([id] ASC)
);


GO

CREATE NONCLUSTERED INDEX [IX_OutbackMateChargeControllerRecords_OneMinuteArchive_RecordDateTime]
    ON [dbo].[OutbackMateChargeControllerRecords_OneMinuteArchive]([recordDateTime] ASC);


GO

