CREATE TABLE [v9].[BmsDeviceConfig] (
    [id]                    BIGINT             IDENTITY (1, 1) NOT NULL,
    [deviceId]              INT                NOT NULL,
    [recordedAt]            DATETIMEOFFSET (7) NOT NULL,
    [cellCount]             TINYINT            NOT NULL,
    [nominalCapacityMah]    BIGINT             NOT NULL,
    [chargingEnabled]       BIT                NOT NULL,
    [dischargingEnabled]    BIT                NOT NULL,
    [balancingEnabled]      BIT                NOT NULL,
    [cellOvpMv]             BIGINT             NOT NULL,
    [cellOvpRecoveryMv]     BIGINT             NOT NULL,
    [cellUvpMv]             BIGINT             NOT NULL,
    [cellUvpRecoveryMv]     BIGINT             NOT NULL,
    [balanceTriggerMv]      BIGINT             NOT NULL,
    [balanceStartVoltageMv] BIGINT             NOT NULL,
    [chargeOcpMa]           BIGINT             NOT NULL,
    [chargeOcpDelayS]       BIGINT             NOT NULL,
    [chargeOcpRecoveryS]    BIGINT             NOT NULL,
    [dischargeOcpMa]        BIGINT             NOT NULL,
    [dischargeOcpDelayS]    BIGINT             NOT NULL,
    [dischargeOcpRecoveryS] BIGINT             NOT NULL,
    [shortCircuitDelayUs]   BIGINT             NOT NULL,
    [shortCircuitRecoveryS] BIGINT             NOT NULL,
    [chargeOtpC]            DECIMAL (9, 2)     NOT NULL,
    [chargeOtpRecoveryC]    DECIMAL (9, 2)     NOT NULL,
    [chargeUtpC]            DECIMAL (9, 2)     NOT NULL,
    [chargeUtpRecoveryC]    DECIMAL (9, 2)     NOT NULL,
    [dischargeOtpC]         DECIMAL (9, 2)     NOT NULL,
    [dischargeOtpRecoveryC] DECIMAL (9, 2)     NOT NULL,
    [mosOtpC]               DECIMAL (9, 2)     NOT NULL,
    [mosOtpRecoveryC]       DECIMAL (9, 2)     NOT NULL,
    CONSTRAINT [PK_BmsDeviceConfig] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsDeviceConfig_BmsDevice] FOREIGN KEY ([deviceId]) REFERENCES [v9].[BmsDevice] ([id]) ON DELETE CASCADE
);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsDeviceConfig_DeviceId_RecordedAt]
    ON [v9].[BmsDeviceConfig] ([deviceId] ASC, [recordedAt] ASC);

GO
