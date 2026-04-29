using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddWeatherRawUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeatherRaw_StationId_RecordedAt",
                schema: "v9",
                table: "WeatherRaw");

            migrationBuilder.CreateIndex(
                name: "IX_WeatherRaw_StationId_RecordedAt",
                schema: "v9",
                table: "WeatherRaw",
                columns: new[] { "StationId", "RecordedAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeatherRaw_StationId_RecordedAt",
                schema: "v9",
                table: "WeatherRaw");

            migrationBuilder.CreateIndex(
                name: "IX_WeatherRaw_StationId_RecordedAt",
                schema: "v9",
                table: "WeatherRaw",
                columns: new[] { "StationId", "RecordedAt" });
        }
    }
}
