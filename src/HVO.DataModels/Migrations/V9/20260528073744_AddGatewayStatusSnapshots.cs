using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddGatewayStatusSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayStatusSnapshot",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GatewayId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HealthState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceFreshnessState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RestState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MqttState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AlertCount = table.Column<int>(type: "int", nullable: false),
                    OutboxPendingCount = table.Column<int>(type: "int", nullable: false),
                    OutboxFailedCount = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayStatusSnapshot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayStatusSnapshot_RecordedAt",
                schema: "v9",
                table: "GatewayStatusSnapshot",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayStatusSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "GatewayStatusSnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayStatusSnapshot_SourceId_RecordedAt",
                schema: "v9",
                table: "GatewayStatusSnapshot",
                columns: new[] { "SourceId", "RecordedAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatewayStatusSnapshot",
                schema: "v9");
        }
    }
}
