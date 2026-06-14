# HVO.DataModels - Entity Framework Core Data Layer

Entity Framework Core data access layer providing database contexts, entity models, and repository patterns for the HVOv9 observatory suite.

## Package Information

- **Target Framework**: .NET 10.0
- **Namespace**: `HVO.DataModels`
- **Type**: Data Access Library
- **Database**: Azure SQL (primary), SQLite (development/test)

## Purpose

Centralized data access layer for:
- Weather station telemetry storage
- Battery management system (BMS) telemetry
- Power system aggregate readings
- Observatory equipment status history
- API key authentication persistence
- Site configuration management

## Structure

```
HVO.DataModels/
├── Data/
│   ├── HvoDbContext.cs            # Legacy dbo-schema DbContext (read reference)
│   ├── HvoV9DbContext.cs          # Main v9-schema DbContext for current development
│   └── HvoV9DbContextFactory.cs   # Design-time factory for EF migrations
├── Extensions/
│   └── ServiceCollectionExtensions.cs  # DI registration helpers
├── Models/
│   ├── AllSkyCameraRecord.cs      # Legacy all-sky camera record
│   ├── CameraRecord.cs            # Legacy generic camera record
│   ├── DavisVantagePro*.cs        # Legacy Davis Vantage Pro models
│   ├── OutbackMate*.cs            # Legacy Outback Mate models
│   ├── SecurityCameraRecord.cs    # Legacy security camera records
│   ├── SkyMonitor.cs              # Legacy sky monitor records
│   ├── V9/                        # Current v9 entity models
│   │   ├── WeatherRaw.cs          # Raw weather readings
│   │   ├── WeatherMinute.cs       # Minute-aggregated weather
│   │   ├── WeatherHourly.cs       # Hourly-aggregated weather
│   │   ├── BmsReading.cs          # BMS battery readings
│   │   ├── BmsDevice.cs           # BMS device inventory
│   │   ├── PowerReading.cs        # Power system readings
│   │   ├── PowerEnergySnapshot.cs # Energy counter snapshots
│   │   ├── ApiKey.cs              # API key authentication
│   │   ├── ImageMetadata.cs       # Image metadata records
│   │   ├── SiteConfiguration.cs   # Runtime site configuration
│   │   ├── GatewayStatusSnapshot.cs # Gateway runtime status
│   │   └── ...                    # Additional v9 entities
│   ├── WeatherCameraRecord.cs     # Legacy weather camera records
│   ├── WeatherSatelliteRecord.cs  # Legacy satellite records
│   └── WebPowerSwitchConfiguration.cs # Legacy PDU config
├── RawModels/
│   ├── DavisVantageProAverage.cs  # Davis average calculations
│   └── WeatherRecordHighLowSummary.cs # High/low summaries
├── Repositories/
│   ├── IRepository.cs             # Generic repository interface
│   └── Repository.cs              # Generic repository implementation
└── Migrations/                    # EF Core migrations
```

## Key Features

### Two DbContexts

- **HvoDbContext** - Legacy `dbo` schema. Read-only reference for backward compatibility. New development targets the v9 schema.
- **HvoV9DbContext** - Current `v9` schema. All new entities, ingest APIs, and read models use this context.

### Repository Pattern

Optional abstraction over EF Core for:
- Testability (easy mocking)
- Consistent data access patterns
- Query encapsulation

## Database Schema

### v9 Schema (Current)

Weather tables:
```sql
CREATE TABLE v9.WeatherRaw (
    Id BIGINT IDENTITY PRIMARY KEY,
    StationId NVARCHAR(100) NOT NULL,
    RecordedAtUtc DATETIME2 NOT NULL,
    OutsideTemperatureF DECIMAL(9,4),
    OutsideHumidityPercent DECIMAL(9,4),
    -- Additional weather fields
);
```

BMS tables:
```sql
CREATE TABLE v9.BmsReadings (
    Id BIGINT IDENTITY PRIMARY KEY,
    DeviceId NVARCHAR(100) NOT NULL,
    RecordedAtUtc DATETIME2 NOT NULL,
    StateOfChargePercent DECIMAL(9,4),
    VoltageV DECIMAL(9,4),
    -- Additional BMS fields
);
```

Power tables:
```sql
CREATE TABLE v9.PowerReadings (
    Id BIGINT IDENTITY PRIMARY KEY,
    SourceId NVARCHAR(100) NOT NULL,
    RecordedAtUtc DATETIME2 NOT NULL,
    PvPowerW DECIMAL(12,4),
    LoadPowerW DECIMAL(12,4),
    -- Additional power fields
);
```

## Configuration

### Connection Strings

```json
{
  "ConnectionStrings": {
    "HualapaiValleyObservatory": "Server=tcp:<server>.database.windows.net;Database=<db>;..."
  }
}
```

### Dependency Injection Setup

```csharp
services.AddDbContext<HvoV9DbContext>(options =>
    options.UseSqlServer(
        configuration.GetConnectionString("HualapaiValleyObservatory")));
```

### Design-Time Factory

```csharp
public class HvoV9DbContextFactory : IDesignTimeDbContextFactory<HvoV9DbContext>
{
    public HvoV9DbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HvoV9DbContext>();
        optionsBuilder.UseSqlServer("...");
        return new HvoV9DbContext(optionsBuilder.Options);
    }
}
```

## Database Migrations

```bash
# From src/HVO.DataModels/
dotnet ef migrations add <Name> --context HvoV9DbContext
dotnet ef database update --context HvoV9DbContext
```

## Dependencies

- **Microsoft.EntityFrameworkCore.SqlServer** - SQL Server / Azure SQL provider
- **Microsoft.EntityFrameworkCore.Tools** - Migration tools
- **HVO.Core** - Core shared library (NuGet)

## Used By

- `HVO.WebSite.v9` - Main website data access (Azure SQL)
- `HVO.Hardware.DavisVantagePro2` - Legacy schema reads
