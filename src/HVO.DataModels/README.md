# HVO.DataModels

Active .NET 10 EF Core library used by the website. SQL Server/Azure SQL is the
canonical provider. InMemory/SQLite are test doubles, not a supported replacement
production schema. The library has no executable startup or HVO.Core dependency;
dependencies are EF Core/SqlServer, design/tools and cryptographic XML support.

## Schema authority

- [HvoV9DbContext](Data/HvoV9DbContext.cs) owns current model configuration and indexes.
- [V9 entities](Models/V9/) own CLR types, table attributes and unit comments.
- [V9 migration chain](Migrations/V9/) and [model snapshot](Migrations/V9/HvoV9DbContextModelSnapshot.cs) own the generated schema.
- [HvoDbContext](Data/HvoDbContext.cs) retains legacy dbo reads/compatibility;
  new entities and ingest/read models use v9.

DbSet names are not table names. These actual singular tables replace the old
illustrative DDL; consult the linked authority for every column/index/relationship.

| Table/entity | Time and identity | Actual types, units and signs |
|---|---|---|
| [v9.WeatherRaw](Models/V9/WeatherRaw.cs) | long Id; DateTime RecordedAt; nullable string StationId | nullable double TemperatureF/DewPointF, HumidityPercent, BarometricPressureInHg, WindSpeedMph/WindGustMph, RainfallInches, SolarRadiationWm2/UvIndex; nullable int WindDirectionDegrees |
| [v9.BmsReading](Models/V9/BmsReading.cs) | long Id; int DeviceId FK to BmsDevice; DateTime RecordedAt at source | long PackVoltageMv/capacity mAh, int CurrentMa, double PowerWatts and temperatures C, byte SocPercent/SohPercent; JK current/power positive charging, negative discharging; cell voltage/resistance are child rows |
| [v9.PowerReading](Models/V9/PowerReading.cs) | long Id; string SourceId, nullable SourceSystem/DeviceId; DateTime RecordedAt; CreatedAt is persistence metadata | nullable double W/V/A/kWh/Hz/% fields; preserve source-native electrical signs and source provenance; do not globally invert battery current/power |
| [v9.WeatherArchive](Models/V9/WeatherArchive.cs) | RecordedAtUtc is the explicit archive exception; ConsoleRecordedAtLocal and CreatedAtUtc are separate | full console archive rather than sparse live weather; actual mappings/scales are entity/context owned |

The context maps observation timestamps to SQL datetime2; use UTC source times
as required by the ingest contracts. RecordedAt is not renamed RecordedAtUtc in
the raw/BMS/general-power tables. Weather/BMS aggregate entities do not establish
that every rollup is populated. Power device/configuration/energy/inverter/MPPT/
gateway and SmartShunt detail models remain separate typed records.
[Observation identity](../../docs/development/power-observation-identity.md),
[retry durability](../../docs/development/canonical-ingest-retries.md),
[UTC history](../../docs/development/power-history-utc.md) and
[weather queries](../../docs/development/canonical-weather-queries.md) govern
replay, content proof, window and freshness behavior.

## Configuration and consumers

The real website receives its `HualapaiValleyObservatory` connection string from
its approved configuration/Key Vault boundary and registers SQL Server contexts.
See [website startup](../HVO.WebSite.v9/Program.cs) and
[website setup](../HVO.WebSite.v9/README.md). Hardware collectors deliver through
typed HTTP APIs and their own SQLite outbox; they do not read the legacy SQL model.
Davis's production astronomy projection depends on [Staging](../HVO.Staging/README.md),
not this library.

## Generate versus apply migrations

The [design-time factory](Data/HvoV9DbContextFactory.cs) uses the literal
`Server=.;Database=HvoV9;Trusted_Connection=True;` stub for migration generation.
It does not select an approved application database, load website secrets or
interpret an operational target. It must not be used as a default apply target.

From the repository root, use the pinned [dotnet-ef manifest](../../.config/dotnet-tools.json)
with the exact [SDK](../../global.json). This generation-only example does not
connect to or update a database:

```bash
dotnet tool restore
dotnet ef migrations add MeaningfulChange --project src/HVO.DataModels --context HvoV9DbContext --output-dir Migrations/V9
```

Inspect generated migration/snapshot diffs and compatibility before any application.
Applying migrations is a separate approved rollout against an explicitly verified
connection/identity with recovery planning; no generic database-update command
here authorizes the factory stub. For a disposable proof, follow the
[owned SQL Server fixture](../../docs/development/sql-server-integration-tests.md):
it creates test-owned databases and applies real clean/prior migration chains
without using application connection settings.

## Local validation and references

After the root locked restore/build, focused hardware-free validation is:

```bash
dotnet test tests/HVO.WebSite.UnitTests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
dotnet test tests/HVO.WebSite.ApiTests --no-build --no-restore --filter "TestCategory!=Integration&TestCategory!=Live"
```

The SQL category needs its separate disposable fixture. Archived
[HVO.Database reference SQL](../HVO.Database/v9/Tables/SmartShuntDetailSnapshot.sql)
remains a direct input to schema tests despite the sqlproj's exclusion from the
32-project solution. Preserve those files until their test consumers are changed.
The [project index](../../docs/README.md#project-documentation-owners) and
[test guide](../../tests/README.md) identify all owners.
