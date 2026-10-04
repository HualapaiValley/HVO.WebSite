using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class PreservePowerObservationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PowerInverterDetailSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerInverterDetailSnapshot");

            migrationBuilder.DropIndex(
                name: "IX_PowerEnergySnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerEnergySnapshot");

            migrationBuilder.DropIndex(
                name: "IX_PowerDeviceInventorySnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerDeviceInventorySnapshot");

            migrationBuilder.DropIndex(
                name: "IX_PowerConfigurationSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerConfigurationSnapshot");

            migrationBuilder.DropIndex(
                name: "IX_GatewayStatusSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "GatewayStatusSnapshot");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve history: reverting uniqueness is possible only before repeated content arrives.
            foreach (var table in new[] { "PowerInverterDetailSnapshot", "PowerEnergySnapshot", "PowerDeviceInventorySnapshot", "PowerConfigurationSnapshot", "GatewayStatusSnapshot" })
            {
                migrationBuilder.Sql($"""
                    IF EXISTS (SELECT 1 FROM [v9].[{table}] GROUP BY [SourceId], [PayloadHash] HAVING COUNT_BIG(*) > 1)
                        THROW 51000, 'Cannot restore historical content uniqueness without losing observations. Keep the current schema or restore a coordinated pre-upgrade backup.', 1;
                    """);
            }
            migrationBuilder.CreateIndex(
                name: "IX_PowerInverterDetailSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerInverterDetailSnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerEnergySnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerEnergySnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerDeviceInventorySnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerDeviceInventorySnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PowerConfigurationSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "PowerConfigurationSnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayStatusSnapshot_SourceId_PayloadHash",
                schema: "v9",
                table: "GatewayStatusSnapshot",
                columns: new[] { "SourceId", "PayloadHash" },
                unique: true);
        }
    }
}
