using System;
using System.Collections.Generic;
using HVO.DataModels.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.DataModels.Data;

public partial class HvoDbContext : DbContext
{
    public HvoDbContext()
    {
    }

    public HvoDbContext(DbContextOptions<HvoDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AllSkyCameraRecord> AllSkyCameraRecords { get; set; }

    public virtual DbSet<CameraRecord> CameraRecords { get; set; }

    public virtual DbSet<DavisVantageProConsoleRecord> DavisVantageProConsoleRecords { get; set; }

    public virtual DbSet<DavisVantageProConsoleRecordsNew> DavisVantageProConsoleRecordsNews { get; set; }

    public virtual DbSet<DavisVantageProConsoleRecordsOneMinuteArchive> DavisVantageProConsoleRecordsOneMinuteArchives { get; set; }

    public virtual DbSet<OutbackMateChargeControllerRecord> OutbackMateChargeControllerRecords { get; set; }

    public virtual DbSet<OutbackMateChargeControllerRecordsNew> OutbackMateChargeControllerRecordsNews { get; set; }

    public virtual DbSet<OutbackMateChargeControllerRecordsOneMinuteArchive> OutbackMateChargeControllerRecordsOneMinuteArchives { get; set; }

    public virtual DbSet<OutbackMateFlexNetRecord> OutbackMateFlexNetRecords { get; set; }

    public virtual DbSet<OutbackMateFlexNetRecordsNew> OutbackMateFlexNetRecordsNews { get; set; }

    public virtual DbSet<OutbackMateFlexNetRecordsOneMinuteArchive> OutbackMateFlexNetRecordsOneMinuteArchives { get; set; }

    public virtual DbSet<OutbackMateInverterChargerRecord> OutbackMateInverterChargerRecords { get; set; }

    public virtual DbSet<OutbackMateInverterChargerRecordsNew> OutbackMateInverterChargerRecordsNews { get; set; }

    public virtual DbSet<OutbackMateInverterChargerRecordsOneMinuteArchive> OutbackMateInverterChargerRecordsOneMinuteArchives { get; set; }

    public virtual DbSet<SecurityCameraRecord> SecurityCameraRecords { get; set; }

    public virtual DbSet<SecurityCameraRecords2> SecurityCameraRecords2s { get; set; }

    public virtual DbSet<SkyMonitor> SkyMonitors { get; set; }

    public virtual DbSet<WeatherCameraRecord> WeatherCameraRecords { get; set; }

    public virtual DbSet<WeatherCameraRecords2> WeatherCameraRecords2s { get; set; }

    public virtual DbSet<WeatherSatelliteRecord> WeatherSatelliteRecords { get; set; }

    public virtual DbSet<WebPowerSwitchConfiguration> WebPowerSwitchConfigurations { get; set; }

    private DbSet<RawModels.WeatherRecordHighLowSummary> WeatherRecordHighLowSummary { get; set; }
    private DbSet<RawModels.DavisVantageProAverage> DavisVantageProAverage { get; set; }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Only configure SQL Server if no other provider has been configured
        // This allows tests to override with in-memory database
        if (!optionsBuilder.IsConfigured)
        {
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
            optionsBuilder.UseSqlServer("Server=tcp:hvo.database.windows.net,1433;Initial Catalog=HualapaiValleyObservatory;Persist Security Info=False;User ID=roys;Password=1qaz!qaz;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AllSkyCameraRecord>(entity =>
        {
            entity.HasIndex(e => new { e.RecordDateTime, e.CameraNumber, e.ImageType }, "IX_AllSkyCameraRecords");

            entity.Property(e => e.StorageLocation).IsUnicode(false);
        });

        modelBuilder.Entity<CameraRecord>(entity =>
        {
            entity.HasIndex(e => new { e.ImageType, e.CameraType }, "IX_CameraRecords_ImageType_CameraNumber_RecordDateTime");

            entity.HasIndex(e => new { e.RecordDateTime, e.CameraNumber, e.ImageType, e.CameraType }, "IX_CameraRecords_RDT_CN_IT_CT").IsDescending(true, false, false, false);

            entity.HasIndex(e => e.CameraType, "IX_CameraType_incIT_incCN");

            entity.Property(e => e.StorageLocation).IsUnicode(false);
        });

        modelBuilder.Entity<DavisVantageProConsoleRecord>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("DavisVantageProConsoleRecords_");

            entity.Property(e => e.Barometer)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("barometer");
            entity.Property(e => e.BarometerTrend).HasColumnName("barometerTrend");
            entity.Property(e => e.ConsoleBatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("consoleBatteryVoltage");
            entity.Property(e => e.DailyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("dailyETAmount");
            entity.Property(e => e.DailyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("dailyRainAmount");
            entity.Property(e => e.ForcastIcons).HasColumnName("forcastIcons");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            entity.Property(e => e.InsideHumidity).HasColumnName("insideHumidity");
            entity.Property(e => e.InsideTemperature)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("insideTemperature");
            entity.Property(e => e.MonthlyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("monthlyETAmount");
            entity.Property(e => e.MonthlyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("monthlyRainAmount");
            entity.Property(e => e.OutsideDewpoint)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideDewpoint");
            entity.Property(e => e.OutsideHeatIndex)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideHeatIndex");
            entity.Property(e => e.OutsideHumidity).HasColumnName("outsideHumidity");
            entity.Property(e => e.OutsideTemperature)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideTemperature");
            entity.Property(e => e.OutsideWindChill)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideWindChill");
            entity.Property(e => e.RainRate)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("rainRate");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.SolarRadiation).HasColumnName("solarRadiation");
            entity.Property(e => e.StormRain)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("stormRain");
            entity.Property(e => e.StormStartDate).HasColumnName("stormStartDate");
            entity.Property(e => e.SunriseTime).HasColumnName("sunriseTime");
            entity.Property(e => e.SunsetTime).HasColumnName("sunsetTime");
            entity.Property(e => e.TenMinuteWindSpeedAverage).HasColumnName("tenMinuteWindSpeedAverage");
            entity.Property(e => e.UvIndex).HasColumnName("uvIndex");
            entity.Property(e => e.WindDirection).HasColumnName("windDirection");
            entity.Property(e => e.WindSpeed).HasColumnName("windSpeed");
            entity.Property(e => e.YearlyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("yearlyETAmount");
            entity.Property(e => e.YearlyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("yearlyRainAmount");
        });

        modelBuilder.Entity<DavisVantageProConsoleRecordsNew>(entity =>
        {
            entity.ToTable("DavisVantageProConsoleRecords_NEW");

            entity.HasIndex(e => e.RecordDateTime, "IX_DavisVantageProConsoleRecords_NEW").IsUnique();

            entity.HasIndex(e => e.RecordDateTime, "IX_DavisVantageProConsoleRecords_NEW_Cover").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Barometer)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("barometer");
            entity.Property(e => e.BarometerTrend).HasColumnName("barometerTrend");
            entity.Property(e => e.ConsoleBatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("consoleBatteryVoltage");
            entity.Property(e => e.DailyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("dailyETAmount");
            entity.Property(e => e.DailyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("dailyRainAmount");
            entity.Property(e => e.ForcastIcons).HasColumnName("forcastIcons");
            entity.Property(e => e.InsideHumidity).HasColumnName("insideHumidity");
            entity.Property(e => e.InsideTemperature)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("insideTemperature");
            entity.Property(e => e.MonthlyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("monthlyETAmount");
            entity.Property(e => e.MonthlyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("monthlyRainAmount");
            entity.Property(e => e.OutsideDewpoint)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideDewpoint");
            entity.Property(e => e.OutsideHeatIndex)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideHeatIndex");
            entity.Property(e => e.OutsideHumidity).HasColumnName("outsideHumidity");
            entity.Property(e => e.OutsideTemperature)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideTemperature");
            entity.Property(e => e.OutsideWindChill)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideWindChill");
            entity.Property(e => e.RainRate)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("rainRate");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.SolarRadiation).HasColumnName("solarRadiation");
            entity.Property(e => e.StormRain)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("stormRain");
            entity.Property(e => e.StormStartDate).HasColumnName("stormStartDate");
            entity.Property(e => e.SunriseTime).HasColumnName("sunriseTime");
            entity.Property(e => e.SunsetTime).HasColumnName("sunsetTime");
            entity.Property(e => e.TenMinuteWindSpeedAverage).HasColumnName("tenMinuteWindSpeedAverage");
            entity.Property(e => e.UvIndex).HasColumnName("uvIndex");
            entity.Property(e => e.WindDirection).HasColumnName("windDirection");
            entity.Property(e => e.WindSpeed).HasColumnName("windSpeed");
            entity.Property(e => e.YearlyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("yearlyETAmount");
            entity.Property(e => e.YearlyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("yearlyRainAmount");
        });

        modelBuilder.Entity<DavisVantageProConsoleRecordsOneMinuteArchive>(entity =>
        {
            entity.ToTable("DavisVantageProConsoleRecords_OneMinuteArchive");

            entity.HasIndex(e => e.RecordDateTime, "IX_DavisVantageProConsoleRecords_OneMinuteArchive_RecordDateTime").IsUnique();

            entity.HasIndex(e => e.RecordDateTime, "IX_DavisVantageProConsoleRecords_OneMinuteArchive_RecordDateTime_Cover");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Barometer)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("barometer");
            entity.Property(e => e.BarometerTrend).HasColumnName("barometerTrend");
            entity.Property(e => e.ConsoleBatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("consoleBatteryVoltage");
            entity.Property(e => e.DailyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("dailyETAmount");
            entity.Property(e => e.DailyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("dailyRainAmount");
            entity.Property(e => e.ForcastIcons).HasColumnName("forcastIcons");
            entity.Property(e => e.InsideHumidity).HasColumnName("insideHumidity");
            entity.Property(e => e.InsideTemperature)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("insideTemperature");
            entity.Property(e => e.MonthlyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("monthlyETAmount");
            entity.Property(e => e.MonthlyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("monthlyRainAmount");
            entity.Property(e => e.OutsideDewpoint)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideDewpoint");
            entity.Property(e => e.OutsideHeatIndex)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideHeatIndex");
            entity.Property(e => e.OutsideHumidity).HasColumnName("outsideHumidity");
            entity.Property(e => e.OutsideTemperature)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideTemperature");
            entity.Property(e => e.OutsideWindChill)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("outsideWindChill");
            entity.Property(e => e.RainRate)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("rainRate");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.SolarRadiation).HasColumnName("solarRadiation");
            entity.Property(e => e.StormRain)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("stormRain");
            entity.Property(e => e.StormStartDate).HasColumnName("stormStartDate");
            entity.Property(e => e.SunriseTime).HasColumnName("sunriseTime");
            entity.Property(e => e.SunsetTime).HasColumnName("sunsetTime");
            entity.Property(e => e.TenMinuteWindSpeedAverage).HasColumnName("tenMinuteWindSpeedAverage");
            entity.Property(e => e.UvIndex).HasColumnName("uvIndex");
            entity.Property(e => e.WindDirection).HasColumnName("windDirection");
            entity.Property(e => e.WindSpeed).HasColumnName("windSpeed");
            entity.Property(e => e.YearlyEtamount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("yearlyETAmount");
            entity.Property(e => e.YearlyRainAmount)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("yearlyRainAmount");
        });

        modelBuilder.Entity<OutbackMateChargeControllerRecord>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("OutbackMateChargeControllerRecords_");

            entity.Property(e => e.ChargerAmps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("chargerAmps");
            entity.Property(e => e.ChargerAuxRelayMode).HasColumnName("chargerAuxRelayMode");
            entity.Property(e => e.ChargerErrorMode).HasColumnName("chargerErrorMode");
            entity.Property(e => e.ChargerMode).HasColumnName("chargerMode");
            entity.Property(e => e.ChargerVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("chargerVoltage");
            entity.Property(e => e.DailyAmpHoursProduced).HasColumnName("dailyAmpHoursProduced");
            entity.Property(e => e.DailyWattHoursProduced).HasColumnName("dailyWattHoursProduced");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            entity.Property(e => e.PvAmps).HasColumnName("pvAmps");
            entity.Property(e => e.PvVoltage).HasColumnName("pvVoltage");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
        });

        modelBuilder.Entity<OutbackMateChargeControllerRecordsNew>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_OutbackMateChargeControllerRecords");

            entity.ToTable("OutbackMateChargeControllerRecords_NEW");

            entity.HasIndex(e => e.RecordDateTime, "IX_OutbackMateChargeControllerRecords_cover01");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChargerAmps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("chargerAmps");
            entity.Property(e => e.ChargerAuxRelayMode).HasColumnName("chargerAuxRelayMode");
            entity.Property(e => e.ChargerErrorMode).HasColumnName("chargerErrorMode");
            entity.Property(e => e.ChargerMode).HasColumnName("chargerMode");
            entity.Property(e => e.ChargerVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("chargerVoltage");
            entity.Property(e => e.DailyAmpHoursProduced).HasColumnName("dailyAmpHoursProduced");
            entity.Property(e => e.DailyWattHoursProduced).HasColumnName("dailyWattHoursProduced");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.PvAmps).HasColumnName("pvAmps");
            entity.Property(e => e.PvVoltage).HasColumnName("pvVoltage");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
        });

        modelBuilder.Entity<OutbackMateChargeControllerRecordsOneMinuteArchive>(entity =>
        {
            entity.ToTable("OutbackMateChargeControllerRecords_OneMinuteArchive");

            entity.HasIndex(e => e.RecordDateTime, "IX_OutbackMateChargeControllerRecords_OneMinuteArchive_RecordDateTime");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChargerAmps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("chargerAmps");
            entity.Property(e => e.ChargerAuxRelayMode).HasColumnName("chargerAuxRelayMode");
            entity.Property(e => e.ChargerErrorMode).HasColumnName("chargerErrorMode");
            entity.Property(e => e.ChargerMode).HasColumnName("chargerMode");
            entity.Property(e => e.ChargerVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("chargerVoltage");
            entity.Property(e => e.DailyAmpHoursProduced).HasColumnName("dailyAmpHoursProduced");
            entity.Property(e => e.DailyWattHoursProduced).HasColumnName("dailyWattHoursProduced");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.PvAmps).HasColumnName("pvAmps");
            entity.Property(e => e.PvVoltage).HasColumnName("pvVoltage");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
        });

        modelBuilder.Entity<OutbackMateFlexNetRecord>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("OutbackMateFlexNetRecords_");

            entity.Property(e => e.BatteryStateOfCharge).HasColumnName("batteryStateOfCharge");
            entity.Property(e => e.BatteryTemperatureC).HasColumnName("batteryTemperatureC");
            entity.Property(e => e.BatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("batteryVoltage");
            entity.Property(e => e.ChargeParamsMet).HasColumnName("chargeParamsMet");
            entity.Property(e => e.ExtraValue)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("extraValue");
            entity.Property(e => e.ExtraValueTypeId).HasColumnName("extraValueTypeId");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.RelayMode).HasColumnName("relayMode");
            entity.Property(e => e.RelayState).HasColumnName("relayState");
            entity.Property(e => e.ShuntAamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntAAmps");
            entity.Property(e => e.ShuntAenabled).HasColumnName("shuntAEnabled");
            entity.Property(e => e.ShuntBamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntBAmps");
            entity.Property(e => e.ShuntBenabled).HasColumnName("shuntBEnabled");
            entity.Property(e => e.ShuntCamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntCAmps");
            entity.Property(e => e.ShuntCenabled).HasColumnName("shuntCEnabled");
        });

        modelBuilder.Entity<OutbackMateFlexNetRecordsNew>(entity =>
        {
            entity.HasKey(e => e.Id)
                .HasName("PK_OutbackMateFlexNetRecords")
                .IsClustered(false);

            entity.ToTable("OutbackMateFlexNetRecords_NEW");

            entity.HasIndex(e => e.RecordDateTime, "IX_OutbackMateFlexNetRecords_cover01");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatteryStateOfCharge).HasColumnName("batteryStateOfCharge");
            entity.Property(e => e.BatteryTemperatureC).HasColumnName("batteryTemperatureC");
            entity.Property(e => e.BatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("batteryVoltage");
            entity.Property(e => e.ChargeParamsMet).HasColumnName("chargeParamsMet");
            entity.Property(e => e.ExtraValue)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("extraValue");
            entity.Property(e => e.ExtraValueTypeId).HasColumnName("extraValueTypeId");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.RelayMode).HasColumnName("relayMode");
            entity.Property(e => e.RelayState).HasColumnName("relayState");
            entity.Property(e => e.ShuntAamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntAAmps");
            entity.Property(e => e.ShuntAenabled).HasColumnName("shuntAEnabled");
            entity.Property(e => e.ShuntBamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntBAmps");
            entity.Property(e => e.ShuntBenabled).HasColumnName("shuntBEnabled");
            entity.Property(e => e.ShuntCamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntCAmps");
            entity.Property(e => e.ShuntCenabled).HasColumnName("shuntCEnabled");
        });

        modelBuilder.Entity<OutbackMateFlexNetRecordsOneMinuteArchive>(entity =>
        {
            entity.ToTable("OutbackMateFlexNetRecords_OneMinuteArchive");

            entity.HasIndex(e => e.RecordDateTime, "IX_OutbackMateFlexNetRecords_OneMinuteArchive_Cover");

            entity.HasIndex(e => e.RecordDateTime, "IX_OutbackMateFlexNetRecords_OneMinuteArchive_RecordDateTime");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatteryStateOfCharge).HasColumnName("batteryStateOfCharge");
            entity.Property(e => e.BatteryTemperatureC).HasColumnName("batteryTemperatureC");
            entity.Property(e => e.BatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("batteryVoltage");
            entity.Property(e => e.ChargeParamsMet).HasColumnName("chargeParamsMet");
            entity.Property(e => e.ExtraValue)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("extraValue");
            entity.Property(e => e.ExtraValueTypeId).HasColumnName("extraValueTypeId");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.RelayMode).HasColumnName("relayMode");
            entity.Property(e => e.RelayState).HasColumnName("relayState");
            entity.Property(e => e.ShuntAamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntAAmps");
            entity.Property(e => e.ShuntAenabled).HasColumnName("shuntAEnabled");
            entity.Property(e => e.ShuntBamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntBAmps");
            entity.Property(e => e.ShuntBenabled).HasColumnName("shuntBEnabled");
            entity.Property(e => e.ShuntCamps)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("shuntCAmps");
            entity.Property(e => e.ShuntCenabled).HasColumnName("shuntCEnabled");
        });

        modelBuilder.Entity<OutbackMateInverterChargerRecord>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("OutbackMateInverterChargerRecords_");

            entity.Property(e => e.AcInputMode).HasColumnName("acInputMode");
            entity.Property(e => e.AcInputVoltage).HasColumnName("acInputVoltage");
            entity.Property(e => e.AcOutputVoltage).HasColumnName("acOutputVoltage");
            entity.Property(e => e.BatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("batteryVoltage");
            entity.Property(e => e.BuyCurrent).HasColumnName("buyCurrent");
            entity.Property(e => e.ChargerCurrent).HasColumnName("chargerCurrent");
            entity.Property(e => e.ErrorMode).HasColumnName("errorMode");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id");
            entity.Property(e => e.InverterCurrent).HasColumnName("inverterCurrent");
            entity.Property(e => e.Misc).HasColumnName("misc");
            entity.Property(e => e.OperationalMode).HasColumnName("operationalMode");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.SellCurrent).HasColumnName("sellCurrent");
            entity.Property(e => e.WarningMode).HasColumnName("warningMode");
        });

        modelBuilder.Entity<OutbackMateInverterChargerRecordsNew>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_OutbackMateInverterChargerRecords");

            entity.ToTable("OutbackMateInverterChargerRecords_NEW");

            entity.HasIndex(e => e.RecordDateTime, "IX_OutbackMateInverterChargerRecords_cover01");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AcInputMode).HasColumnName("acInputMode");
            entity.Property(e => e.AcInputVoltage).HasColumnName("acInputVoltage");
            entity.Property(e => e.AcOutputVoltage).HasColumnName("acOutputVoltage");
            entity.Property(e => e.BatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("batteryVoltage");
            entity.Property(e => e.BuyCurrent).HasColumnName("buyCurrent");
            entity.Property(e => e.ChargerCurrent).HasColumnName("chargerCurrent");
            entity.Property(e => e.ErrorMode).HasColumnName("errorMode");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.InverterCurrent).HasColumnName("inverterCurrent");
            entity.Property(e => e.Misc).HasColumnName("misc");
            entity.Property(e => e.OperationalMode).HasColumnName("operationalMode");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.SellCurrent).HasColumnName("sellCurrent");
            entity.Property(e => e.WarningMode).HasColumnName("warningMode");
        });

        modelBuilder.Entity<OutbackMateInverterChargerRecordsOneMinuteArchive>(entity =>
        {
            entity.ToTable("OutbackMateInverterChargerRecords_OneMinuteArchive");

            entity.HasIndex(e => e.RecordDateTime, "IX_OutbackMateInverterChargerRecords_OneMinuteArchive_RecordDateTime");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AcInputMode).HasColumnName("acInputMode");
            entity.Property(e => e.AcInputVoltage).HasColumnName("acInputVoltage");
            entity.Property(e => e.AcOutputVoltage).HasColumnName("acOutputVoltage");
            entity.Property(e => e.BatteryVoltage)
                .HasColumnType("decimal(9, 2)")
                .HasColumnName("batteryVoltage");
            entity.Property(e => e.BuyCurrent).HasColumnName("buyCurrent");
            entity.Property(e => e.ChargerCurrent).HasColumnName("chargerCurrent");
            entity.Property(e => e.ErrorMode).HasColumnName("errorMode");
            entity.Property(e => e.HubPort).HasColumnName("hubPort");
            entity.Property(e => e.InverterCurrent).HasColumnName("inverterCurrent");
            entity.Property(e => e.Misc).HasColumnName("misc");
            entity.Property(e => e.OperationalMode).HasColumnName("operationalMode");
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");
            entity.Property(e => e.SellCurrent).HasColumnName("sellCurrent");
            entity.Property(e => e.WarningMode).HasColumnName("warningMode");
        });

        modelBuilder.Entity<SecurityCameraRecord>(entity =>
        {
            entity.HasIndex(e => new { e.RecordDateTime, e.CameraNumber, e.ImageType }, "IX_SecurityCameraRecords");

            entity.Property(e => e.StorageLocation).IsUnicode(false);
        });

        modelBuilder.Entity<SecurityCameraRecords2>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("SecurityCameraRecords2");

            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.StorageLocation).IsUnicode(false);
        });

        modelBuilder.Entity<SkyMonitor>(entity =>
        {
            entity.ToTable("SkyMonitor");

            entity.HasIndex(e => e.RecordDateTime, "IX_SkyMonitor_RecordDateTime");

            entity.Property(e => e.AmbientTemperature).HasColumnType("decimal(9, 2)");
            entity.Property(e => e.Gain)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Ir)
                .HasColumnType("decimal(9, 4)")
                .HasColumnName("IR");
            entity.Property(e => e.Lux).HasColumnType("decimal(9, 4)");
            entity.Property(e => e.SkyTemperature).HasColumnType("decimal(9, 2)");
            entity.Property(e => e.Visible).HasColumnType("decimal(9, 4)");
        });

        modelBuilder.Entity<WeatherCameraRecord>(entity =>
        {
            entity.HasIndex(e => new { e.RecordDateTime, e.CameraNumber, e.ImageType }, "IX_WeatherCameraRecords");

            entity.Property(e => e.StorageLocation).IsUnicode(false);
        });

        modelBuilder.Entity<WeatherCameraRecords2>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("WeatherCameraRecords2");

            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.StorageLocation).IsUnicode(false);
        });

        modelBuilder.Entity<WeatherSatelliteRecord>(entity =>
        {
            entity.HasIndex(e => new { e.RecordDateTime, e.ImageType }, "IX_WeatherSatelliteRecords");

            entity.Property(e => e.StorageLocation).IsUnicode(false);
        });

        modelBuilder.Entity<WebPowerSwitchConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__WebPower__3214EC07C8201E74");

            entity.HasIndex(e => e.SerialNumber, "IX_WebPowerSwitchConfiguration_SerialNumber").IsUnique();

            entity.Property(e => e.Address)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Password)
                .HasMaxLength(25)
                .IsUnicode(false);
            entity.Property(e => e.SerialNumber)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Username)
                .HasMaxLength(25)
                .IsUnicode(false);
        });

        modelBuilder.Entity<RawModels.WeatherRecordHighLowSummary>(entity =>
                {
                    entity.HasKey(e => new { e.StartRecordDateTime, e.EndRecordDateTime });
                    entity.Property(e => e.BarometerHigh).HasColumnName("barometerHigh").HasColumnType("decimal(9,2)");
                    entity.Property(e => e.BarometerLow).HasColumnName("barometerLow").HasColumnType("decimal(9,2)");

                    entity.Property(e => e.InsideTemperatureHigh).HasColumnName("insideTemperatureHigh").HasColumnType("decimal(9,2)");
                    entity.Property(e => e.InsideTemperatureLow).HasColumnName("insideTemperatureLow").HasColumnType("decimal(9,2)");

                    entity.Property(e => e.OutsideDewpointHigh).HasColumnName("outsideDewpointHigh").HasColumnType("decimal(9,2)");
                    entity.Property(e => e.OutsideDewpointLow).HasColumnName("outsideDewpointLow").HasColumnType("decimal(9,2)");

                    entity.Property(e => e.OutsideHeatIndexHigh).HasColumnName("outsideHeatIndexHigh").HasColumnType("decimal(9,2)");
                    entity.Property(e => e.OutsideHeatIndexLow).HasColumnName("outsideHeatIndexLow").HasColumnType("decimal(9,2)");

                    entity.Property(e => e.OutsideTemperatureHigh).HasColumnName("outsideTemperatureHigh").HasColumnType("decimal(9,2)");
                    entity.Property(e => e.OutsideTemperatureLow).HasColumnName("outsideTemperatureLow").HasColumnType("decimal(9,2)");

                    entity.Property(e => e.OutsideWindChillHigh).HasColumnName("outsideWindChillHigh").HasColumnType("decimal(9,2)");
                    entity.Property(e => e.OutsideWindChillLow).HasColumnName("outsideWindChillLow").HasColumnType("decimal(9,2)");
                });

        modelBuilder.Entity<RawModels.DavisVantageProAverage>(entity =>
        {
            entity.HasKey(e => e.RecordDateTime);
            entity.Property(e => e.RecordDateTime).HasColumnName("recordDateTime");

            entity.Property(e => e.Barometer).HasColumnName("barometer").HasColumnType("decimal(9,2)");
            entity.Property(e => e.InsideHumidity).HasColumnName("insideHumidity");
            entity.Property(e => e.InsideTemperature).HasColumnName("insideTemperature").HasColumnType("decimal(9,2)");
            entity.Property(e => e.OutsideDewpoint).HasColumnName("outsideDewpoint").HasColumnType("decimal(9,2)");
            entity.Property(e => e.OutsideHumidity).HasColumnName("outsideHumidity");
            entity.Property(e => e.OutsideTemperature).HasColumnName("outsideTemperature").HasColumnType("decimal(9,2)");
            entity.Property(e => e.SolarRadiation).HasColumnName("solarRadiation");
            entity.Property(e => e.WindDirection).HasColumnName("windDirection");
            entity.Property(e => e.WindSpeed).HasColumnName("windSpeed");
            entity.Property(e => e.WindSpeedHigh).HasColumnName("windSpeedHigh");
            entity.Property(e => e.WindSpeedLow).HasColumnName("windSpeedLow");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

public virtual async Task<RawModels.WeatherRecordHighLowSummary?> GetWeatherRecordHighLowSummary(DateTimeOffset startRecordDateTime, DateTimeOffset endRecordDateTime)
    {
        var p = new Microsoft.Data.SqlClient.SqlParameter[]
        {
                new Microsoft.Data.SqlClient.SqlParameter("@iStartRecordDateTime", System.Data.SqlDbType.DateTimeOffset) { Value = startRecordDateTime },
                new Microsoft.Data.SqlClient.SqlParameter("@iEndRecordDateTime", System.Data.SqlDbType.DateTimeOffset) { Value = endRecordDateTime }
        };

        var r = await this.WeatherRecordHighLowSummary.FromSqlRaw("sp__GetWeatherRecordHighLowSummary @iStartRecordDateTime, @iEndRecordDateTime", p)
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);

        return r.FirstOrDefault();
    }

    public virtual async Task<IEnumerable<RawModels.DavisVantageProAverage>> GetDavisVantageProOneMinuteAverage(DateTimeOffset startRecordDateTime, DateTimeOffset endRecordDateTime)
    {
        var p = new Microsoft.Data.SqlClient.SqlParameter[]
        {
                new Microsoft.Data.SqlClient.SqlParameter("@iRecordDateTimeStart", System.Data.SqlDbType.DateTimeOffset) { Value = startRecordDateTime },
                new Microsoft.Data.SqlClient.SqlParameter("@iRecordDateTimeEnd", System.Data.SqlDbType.DateTimeOffset) { Value = endRecordDateTime }
        };

        return await this.DavisVantageProAverage.FromSqlRaw("ef__GetDavisVantageProOneMinuteAverage @iRecordDateTimeStart, @iRecordDateTimeEnd", p)
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);
    }    
}
