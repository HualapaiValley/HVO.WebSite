using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddPowerReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PowerReading",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PvPowerW = table.Column<double>(type: "float", nullable: true),
                    LoadPowerW = table.Column<double>(type: "float", nullable: true),
                    GridPowerW = table.Column<double>(type: "float", nullable: true),
                    BatteryPowerW = table.Column<double>(type: "float", nullable: true),
                    SystemPowerW = table.Column<double>(type: "float", nullable: true),
                    BatteryStateOfChargePercent = table.Column<double>(type: "float", nullable: true),
                    BatteryVoltageV = table.Column<double>(type: "float", nullable: true),
                    BatteryCurrentA = table.Column<double>(type: "float", nullable: true),
                    BatteryCapacityKwh = table.Column<double>(type: "float", nullable: true),
                    GridVoltageV = table.Column<double>(type: "float", nullable: true),
                    GridFrequencyHz = table.Column<double>(type: "float", nullable: true),
                    OutputVoltageV = table.Column<double>(type: "float", nullable: true),
                    OutputFrequencyHz = table.Column<double>(type: "float", nullable: true),
                    LoadPercentage = table.Column<double>(type: "float", nullable: true),
                    InverterMode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OutputSourcePriority = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChargerSourcePriority = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerReading", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PowerReading_RecordedAt",
                schema: "v9",
                table: "PowerReading",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PowerReading_SourceId_RecordedAt",
                schema: "v9",
                table: "PowerReading",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerReading_SourceSystem_RecordedAt",
                schema: "v9",
                table: "PowerReading",
                columns: new[] { "SourceSystem", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PowerReading",
                schema: "v9");
        }
    }
}
