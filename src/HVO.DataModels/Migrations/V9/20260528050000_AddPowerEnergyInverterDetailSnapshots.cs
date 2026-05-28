using System;
using HVO.DataModels.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    [DbContext(typeof(HvoV9DbContext))]
    [Migration("20260528050000_AddPowerEnergyInverterDetailSnapshots")]
    public partial class AddPowerEnergyInverterDetailSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PowerEnergySnapshot",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CounterCount = table.Column<int>(type: "int", nullable: false),
                    CounterResetDetected = table.Column<bool>(type: "bit", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_PowerEnergySnapshot", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PowerInverterDetailSnapshot",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PvStringCount = table.Column<int>(type: "int", nullable: false),
                    StatusCount = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_PowerInverterDetailSnapshot", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_PowerEnergySnapshot_RecordedAt",
                schema: "v9",
                table: "PowerEnergySnapshot",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PowerEnergySnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerEnergySnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerEnergySnapshot_SourceId_RecordedAt",
                schema: "v9",
                table: "PowerEnergySnapshot",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerInverterDetailSnapshot_RecordedAt",
                schema: "v9",
                table: "PowerInverterDetailSnapshot",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PowerInverterDetailSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerInverterDetailSnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerInverterDetailSnapshot_SourceId_RecordedAt",
                schema: "v9",
                table: "PowerInverterDetailSnapshot",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PowerEnergySnapshot", schema: "v9");
            migrationBuilder.DropTable(name: "PowerInverterDetailSnapshot", schema: "v9");
        }
    }
}
