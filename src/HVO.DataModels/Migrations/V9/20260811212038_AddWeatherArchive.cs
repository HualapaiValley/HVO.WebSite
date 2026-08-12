using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddWeatherArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeatherArchive",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsoleRecordedAtLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchiveIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    TemperatureF = table.Column<double>(type: "float", nullable: true),
                    HighTemperatureF = table.Column<double>(type: "float", nullable: true),
                    LowTemperatureF = table.Column<double>(type: "float", nullable: true),
                    InsideTemperatureF = table.Column<double>(type: "float", nullable: true),
                    HumidityPercent = table.Column<double>(type: "float", nullable: true),
                    InsideHumidityPercent = table.Column<double>(type: "float", nullable: true),
                    BarometricPressureInHg = table.Column<double>(type: "float", nullable: true),
                    WindSpeedMph = table.Column<double>(type: "float", nullable: true),
                    WindGustMph = table.Column<double>(type: "float", nullable: true),
                    WindDirectionDegrees = table.Column<double>(type: "float", nullable: true),
                    WindGustDirectionDegrees = table.Column<double>(type: "float", nullable: true),
                    WindSamples = table.Column<int>(type: "int", nullable: false),
                    RainfallInches = table.Column<double>(type: "float", nullable: true),
                    RainRateInchesPerHour = table.Column<double>(type: "float", nullable: true),
                    SolarRadiationWm2 = table.Column<double>(type: "float", nullable: true),
                    HighSolarRadiationWm2 = table.Column<double>(type: "float", nullable: true),
                    UvIndex = table.Column<double>(type: "float", nullable: true),
                    HighUvIndex = table.Column<double>(type: "float", nullable: true),
                    EtInches = table.Column<double>(type: "float", nullable: true),
                    ForecastRule = table.Column<int>(type: "int", nullable: true),
                    ForecastString = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DownloadRecordType = table.Column<int>(type: "int", nullable: false),
                    LeafTemp1F = table.Column<double>(type: "float", nullable: true),
                    LeafTemp2F = table.Column<double>(type: "float", nullable: true),
                    LeafWetnessJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoilTemperaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtraHumiditiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtraTemperaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SoilMoisturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherArchive", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherArchive_RecordedAtUtc",
                schema: "v9",
                table: "WeatherArchive",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WeatherArchive_StationId_RecordedAtUtc",
                schema: "v9",
                table: "WeatherArchive",
                columns: new[] { "StationId", "RecordedAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeatherArchive",
                schema: "v9");
        }
    }
}
