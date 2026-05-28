using System;
using HVO.DataModels.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    [DbContext(typeof(HvoV9DbContext))]
    [Migration("20260528040000_AddPowerInventoryConfigurationSnapshots")]
    public partial class AddPowerInventoryConfigurationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PowerConfigurationSnapshot",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettingCount = table.Column<int>(type: "int", nullable: false),
                    CommandCapabilityCount = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerConfigurationSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PowerDeviceInventorySnapshot",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RestMetricCount = table.Column<int>(type: "int", nullable: false),
                    MqttEntityCount = table.Column<int>(type: "int", nullable: false),
                    MqttStateTopicCount = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerDeviceInventorySnapshot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PowerConfigurationSnapshot_RecordedAt",
                schema: "v9",
                table: "PowerConfigurationSnapshot",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PowerConfigurationSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerConfigurationSnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerConfigurationSnapshot_SourceId_RecordedAt",
                schema: "v9",
                table: "PowerConfigurationSnapshot",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerDeviceInventorySnapshot_RecordedAt",
                schema: "v9",
                table: "PowerDeviceInventorySnapshot",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PowerDeviceInventorySnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerDeviceInventorySnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerDeviceInventorySnapshot_SourceId_RecordedAt",
                schema: "v9",
                table: "PowerDeviceInventorySnapshot",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PowerConfigurationSnapshot",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "PowerDeviceInventorySnapshot",
                schema: "v9");
        }
    }
}
