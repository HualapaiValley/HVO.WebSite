using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddBmsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BmsSite",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsSite", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BmsDevice",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsDevice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BmsDevice_BmsSite_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "v9",
                        principalTable: "BmsSite",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BmsAlarm",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    AlarmBitmask = table.Column<long>(type: "bigint", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsAlarm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BmsAlarm_BmsDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "v9",
                        principalTable: "BmsDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BmsDeviceConfig",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CellCount = table.Column<byte>(type: "tinyint", nullable: false),
                    NominalCapacityMah = table.Column<long>(type: "bigint", nullable: false),
                    ChargingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DischargingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    BalancingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CellOvpMv = table.Column<long>(type: "bigint", nullable: false),
                    CellOvpRecoveryMv = table.Column<long>(type: "bigint", nullable: false),
                    CellUvpMv = table.Column<long>(type: "bigint", nullable: false),
                    CellUvpRecoveryMv = table.Column<long>(type: "bigint", nullable: false),
                    BalanceTriggerMv = table.Column<long>(type: "bigint", nullable: false),
                    BalanceStartVoltageMv = table.Column<long>(type: "bigint", nullable: false),
                    ChargeOcpMa = table.Column<long>(type: "bigint", nullable: false),
                    ChargeOcpDelayS = table.Column<long>(type: "bigint", nullable: false),
                    ChargeOcpRecoveryS = table.Column<long>(type: "bigint", nullable: false),
                    DischargeOcpMa = table.Column<long>(type: "bigint", nullable: false),
                    DischargeOcpDelayS = table.Column<long>(type: "bigint", nullable: false),
                    DischargeOcpRecoveryS = table.Column<long>(type: "bigint", nullable: false),
                    ShortCircuitDelayUs = table.Column<long>(type: "bigint", nullable: false),
                    ShortCircuitRecoveryS = table.Column<long>(type: "bigint", nullable: false),
                    ChargeOtpC = table.Column<double>(type: "float", nullable: false),
                    ChargeOtpRecoveryC = table.Column<double>(type: "float", nullable: false),
                    ChargeUtpC = table.Column<double>(type: "float", nullable: false),
                    ChargeUtpRecoveryC = table.Column<double>(type: "float", nullable: false),
                    DischargeOtpC = table.Column<double>(type: "float", nullable: false),
                    DischargeOtpRecoveryC = table.Column<double>(type: "float", nullable: false),
                    MosOtpC = table.Column<double>(type: "float", nullable: false),
                    MosOtpRecoveryC = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsDeviceConfig", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BmsDeviceConfig_BmsDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "v9",
                        principalTable: "BmsDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BmsDeviceInfo",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Hardware = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Firmware = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ManufacturingDate = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    UserData = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsDeviceInfo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BmsDeviceInfo_BmsDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "v9",
                        principalTable: "BmsDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BmsReading",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PackVoltageMv = table.Column<long>(type: "bigint", nullable: false),
                    CurrentMa = table.Column<int>(type: "int", nullable: false),
                    PowerWatts = table.Column<double>(type: "float", nullable: false),
                    SocPercent = table.Column<byte>(type: "tinyint", nullable: false),
                    SohPercent = table.Column<byte>(type: "tinyint", nullable: false),
                    RemainingCapacityMah = table.Column<long>(type: "bigint", nullable: false),
                    NominalCapacityMah = table.Column<long>(type: "bigint", nullable: false),
                    CycleCount = table.Column<long>(type: "bigint", nullable: false),
                    CycleCapacityMah = table.Column<long>(type: "bigint", nullable: false),
                    BatteryTemp1C = table.Column<double>(type: "float", nullable: false),
                    BatteryTemp2C = table.Column<double>(type: "float", nullable: false),
                    PowerTubeC = table.Column<double>(type: "float", nullable: false),
                    BalancingActive = table.Column<bool>(type: "bit", nullable: false),
                    BalancingCurrentMa = table.Column<double>(type: "float", nullable: false),
                    DeltaCellVoltageMv = table.Column<int>(type: "int", nullable: false),
                    AlarmBitmask = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsReading", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BmsReading_BmsDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "v9",
                        principalTable: "BmsDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BmsReadingHourly",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AvgPackVoltageMv = table.Column<long>(type: "bigint", nullable: false),
                    AvgCurrentMa = table.Column<int>(type: "int", nullable: false),
                    MaxCurrentMa = table.Column<int>(type: "int", nullable: false),
                    MinCurrentMa = table.Column<int>(type: "int", nullable: false),
                    AvgPowerWatts = table.Column<double>(type: "float", nullable: false),
                    MaxPowerWatts = table.Column<double>(type: "float", nullable: false),
                    AvgSocPercent = table.Column<double>(type: "float", nullable: false),
                    MinSocPercent = table.Column<double>(type: "float", nullable: false),
                    MaxBatteryTemp1C = table.Column<double>(type: "float", nullable: false),
                    MaxBatteryTemp2C = table.Column<double>(type: "float", nullable: false),
                    MaxPowerTubeC = table.Column<double>(type: "float", nullable: false),
                    MaxDeltaCellMv = table.Column<int>(type: "int", nullable: false),
                    AlarmCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsReadingHourly", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BmsReadingHourly_BmsDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "v9",
                        principalTable: "BmsDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BmsReadingMinute",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AvgPackVoltageMv = table.Column<long>(type: "bigint", nullable: false),
                    AvgCurrentMa = table.Column<int>(type: "int", nullable: false),
                    MaxCurrentMa = table.Column<int>(type: "int", nullable: false),
                    MinCurrentMa = table.Column<int>(type: "int", nullable: false),
                    AvgPowerWatts = table.Column<double>(type: "float", nullable: false),
                    MaxPowerWatts = table.Column<double>(type: "float", nullable: false),
                    AvgSocPercent = table.Column<double>(type: "float", nullable: false),
                    MinSocPercent = table.Column<double>(type: "float", nullable: false),
                    MaxBatteryTemp1C = table.Column<double>(type: "float", nullable: false),
                    MaxBatteryTemp2C = table.Column<double>(type: "float", nullable: false),
                    MaxPowerTubeC = table.Column<double>(type: "float", nullable: false),
                    MaxDeltaCellMv = table.Column<int>(type: "int", nullable: false),
                    AlarmCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsReadingMinute", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BmsReadingMinute_BmsDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "v9",
                        principalTable: "BmsDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BmsCellResistance",
                schema: "v9",
                columns: table => new
                {
                    ReadingId = table.Column<long>(type: "bigint", nullable: false),
                    CellIndex = table.Column<byte>(type: "tinyint", nullable: false),
                    ResistanceMOhm = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsCellResistance", x => new { x.ReadingId, x.CellIndex });
                    table.ForeignKey(
                        name: "FK_BmsCellResistance_BmsReading_ReadingId",
                        column: x => x.ReadingId,
                        principalSchema: "v9",
                        principalTable: "BmsReading",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BmsCellVoltage",
                schema: "v9",
                columns: table => new
                {
                    ReadingId = table.Column<long>(type: "bigint", nullable: false),
                    CellIndex = table.Column<byte>(type: "tinyint", nullable: false),
                    VoltageMv = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BmsCellVoltage", x => new { x.ReadingId, x.CellIndex });
                    table.ForeignKey(
                        name: "FK_BmsCellVoltage_BmsReading_ReadingId",
                        column: x => x.ReadingId,
                        principalSchema: "v9",
                        principalTable: "BmsReading",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BmsAlarm_DeviceId_ClearedAt",
                schema: "v9",
                table: "BmsAlarm",
                columns: new[] { "DeviceId", "ClearedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BmsDevice_Address",
                schema: "v9",
                table: "BmsDevice",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BmsDevice_SiteId",
                schema: "v9",
                table: "BmsDevice",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_BmsDeviceConfig_DeviceId_RecordedAt",
                schema: "v9",
                table: "BmsDeviceConfig",
                columns: new[] { "DeviceId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BmsDeviceInfo_DeviceId_RecordedAt",
                schema: "v9",
                table: "BmsDeviceInfo",
                columns: new[] { "DeviceId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BmsReading_DeviceId_RecordedAt",
                schema: "v9",
                table: "BmsReading",
                columns: new[] { "DeviceId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BmsReading_RecordedAt",
                schema: "v9",
                table: "BmsReading",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BmsReadingHourly_DeviceId_PeriodStart",
                schema: "v9",
                table: "BmsReadingHourly",
                columns: new[] { "DeviceId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BmsReadingMinute_DeviceId_PeriodStart",
                schema: "v9",
                table: "BmsReadingMinute",
                columns: new[] { "DeviceId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BmsSite_Name",
                schema: "v9",
                table: "BmsSite",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BmsAlarm",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsCellResistance",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsCellVoltage",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsDeviceConfig",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsDeviceInfo",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsReadingHourly",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsReadingMinute",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsReading",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsDevice",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "BmsSite",
                schema: "v9");
        }
    }
}
