using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class InitialV9Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "v9");

            migrationBuilder.CreateTable(
                name: "AlertLog",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Acknowledged = table.Column<bool>(type: "bit", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageMetadata",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CameraId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ImageType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    WidthPx = table.Column<int>(type: "int", nullable: true),
                    HeightPx = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteConfiguration",
                schema: "v9",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteConfiguration", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "WeatherHourly",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AvgTemperatureF = table.Column<double>(type: "float", nullable: true),
                    MinTemperatureF = table.Column<double>(type: "float", nullable: true),
                    MaxTemperatureF = table.Column<double>(type: "float", nullable: true),
                    AvgHumidityPercent = table.Column<double>(type: "float", nullable: true),
                    AvgDewPointF = table.Column<double>(type: "float", nullable: true),
                    AvgBarometricPressureInHg = table.Column<double>(type: "float", nullable: true),
                    AvgWindSpeedMph = table.Column<double>(type: "float", nullable: true),
                    MaxWindGustMph = table.Column<double>(type: "float", nullable: true),
                    DominantWindDirectionDegrees = table.Column<int>(type: "int", nullable: true),
                    TotalRainfallInches = table.Column<double>(type: "float", nullable: true),
                    AvgSolarRadiationWm2 = table.Column<double>(type: "float", nullable: true),
                    MaxUvIndex = table.Column<double>(type: "float", nullable: true),
                    StationId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherHourly", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeatherMinute",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AvgTemperatureF = table.Column<double>(type: "float", nullable: true),
                    AvgHumidityPercent = table.Column<double>(type: "float", nullable: true),
                    AvgDewPointF = table.Column<double>(type: "float", nullable: true),
                    AvgBarometricPressureInHg = table.Column<double>(type: "float", nullable: true),
                    AvgWindSpeedMph = table.Column<double>(type: "float", nullable: true),
                    MaxWindGustMph = table.Column<double>(type: "float", nullable: true),
                    DominantWindDirectionDegrees = table.Column<int>(type: "int", nullable: true),
                    TotalRainfallInches = table.Column<double>(type: "float", nullable: true),
                    AvgSolarRadiationWm2 = table.Column<double>(type: "float", nullable: true),
                    MaxUvIndex = table.Column<double>(type: "float", nullable: true),
                    StationId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherMinute", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeatherRaw",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TemperatureF = table.Column<double>(type: "float", nullable: true),
                    HumidityPercent = table.Column<double>(type: "float", nullable: true),
                    DewPointF = table.Column<double>(type: "float", nullable: true),
                    BarometricPressureInHg = table.Column<double>(type: "float", nullable: true),
                    WindSpeedMph = table.Column<double>(type: "float", nullable: true),
                    WindGustMph = table.Column<double>(type: "float", nullable: true),
                    WindDirectionDegrees = table.Column<int>(type: "int", nullable: true),
                    RainfallInches = table.Column<double>(type: "float", nullable: true),
                    SolarRadiationWm2 = table.Column<double>(type: "float", nullable: true),
                    UvIndex = table.Column<double>(type: "float", nullable: true),
                    StationId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherRaw", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertLog_Acknowledged",
                schema: "v9",
                table: "AlertLog",
                column: "Acknowledged");

            migrationBuilder.CreateIndex(
                name: "IX_AlertLog_OccurredAt",
                schema: "v9",
                table: "AlertLog",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMetadata_CameraId_CapturedAt",
                schema: "v9",
                table: "ImageMetadata",
                columns: new[] { "CameraId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageMetadata_CapturedAt",
                schema: "v9",
                table: "ImageMetadata",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WeatherHourly_PeriodStart",
                schema: "v9",
                table: "WeatherHourly",
                column: "PeriodStart",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeatherHourly_StationId_PeriodStart",
                schema: "v9",
                table: "WeatherHourly",
                columns: new[] { "StationId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherMinute_PeriodStart",
                schema: "v9",
                table: "WeatherMinute",
                column: "PeriodStart",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeatherMinute_StationId_PeriodStart",
                schema: "v9",
                table: "WeatherMinute",
                columns: new[] { "StationId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherRaw_RecordedAt",
                schema: "v9",
                table: "WeatherRaw",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WeatherRaw_StationId_RecordedAt",
                schema: "v9",
                table: "WeatherRaw",
                columns: new[] { "StationId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertLog",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "ImageMetadata",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "SiteConfiguration",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "WeatherHourly",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "WeatherMinute",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "WeatherRaw",
                schema: "v9");
        }
    }
}
