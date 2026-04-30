CREATE TABLE [v9].[BmsDeviceConfig] (
    [id]                    BIGINT             IDENTITY (1, 1) NOT NULL,
    [deviceId]              INT                NOT NULL,
    [recordedAt]            DATETIME2          NOT NULL,
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
    [chargeOtpC]            FLOAT              NOT NULL,
    [chargeOtpRecoveryC]    FLOAT              NOT NULL,
    [chargeUtpC]            FLOAT              NOT NULL,
    [chargeUtpRecoveryC]    FLOAT              NOT NULL,
    [dischargeOtpC]         FLOAT              NOT NULL,
    [dischargeOtpRecoveryC] FLOAT              NOT NULL,
    [mosOtpC]               FLOAT              NOT NULL,
    [mosOtpRecoveryC]       FLOAT              NOT NULL,
    CONSTRAINT [PK_BmsDeviceConfig] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [FK_BmsDeviceConfig_BmsDevice] FOREIGN KEY ([deviceId]) REFERENCES [v9].[BmsDevice] ([id]) ON DELETE CASCADE
);

GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_BmsDeviceConfig_DeviceId_RecordedAt]
    ON [v9].[BmsDeviceConfig] ([deviceId] ASC, [recordedAt] ASC);

GO
