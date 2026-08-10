using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddPowerMpptDetailSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PowerMpptDetailSnapshot",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TrackerCount = table.Column<int>(type: "int", nullable: false),
                    TemperatureCount = table.Column<int>(type: "int", nullable: false),
                    DiagnosticCount = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerMpptDetailSnapshot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PowerMpptDetailSnapshot_RecordedAt",
                schema: "v9",
                table: "PowerMpptDetailSnapshot",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PowerMpptDetailSnapshot_SourceId_RecordedAt",
                schema: "v9",
                table: "PowerMpptDetailSnapshot",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PowerMpptDetailSnapshot",
                schema: "v9");
        }
    }
}
