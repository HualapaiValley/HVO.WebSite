using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddSmartShuntDetailSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartShuntDetailSnapshot",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAh = table.Column<double>(type: "float", nullable: true),
                    RemainingMinutes = table.Column<double>(type: "float", nullable: true),
                    StarterVoltageV = table.Column<double>(type: "float", nullable: true),
                    TemperatureC = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartShuntDetailSnapshot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartShuntDetailSnapshot_RecordedAt",
                schema: "v9",
                table: "SmartShuntDetailSnapshot",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SmartShuntDetailSnapshot_SourceId_RecordedAt",
                schema: "v9",
                table: "SmartShuntDetailSnapshot",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartShuntDetailSnapshot",
                schema: "v9");
        }
    }
}
