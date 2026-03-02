# HVO.DataModels - Entity Framework Core Data Layer

Entity Framework Core data access layer providing database contexts, entity models, and repository patterns for the HVOv9 observatory suite.

## 📦 Package Information

- **Target Framework**: .NET 9.0
- **Namespace**: `HVO.DataModels`
- **Type**: Data Access Library
- **Database**: SQLite (configurable)

## 🎯 Purpose

Centralized data access layer for:
- Weather station telemetry storage
- Observatory equipment status history
- Configuration persistence
- Time-series data management
- Sky monitoring data archives

## 📁 Structure

```
HVO.DataModels/
├── Data/
│   ├── ApplicationDbContext.cs       # Main EF Core DbContext
│   ├── WeatherDbContext.cs          # Weather-specific context
│   └── Configurations/              # Entity type configurations
├── Models/
│   ├── WeatherDataPoint.cs         # Weather telemetry entities
│   ├── Equipment/                  # Equipment status models
│   └── Configuration/              # App configuration models
├── RawModels/
│   ├── RawWeatherData.cs           # Pre-processed sensor data
│   └── RawSkyData.cs               # Raw sky monitor frames
├── Repositories/
│   ├── IWeatherRepository.cs       # Repository interface
│   ├── WeatherRepository.cs        # Weather data repository
│   └── BaseRepository.cs           # Generic repository base
└── Extensions/
    ├── ServiceCollectionExtensions.cs  # DI setup extensions
    └── QueryExtensions.cs              # LINQ helpers
```

## 🔑 Key Features

### Multiple DbContexts
- **ApplicationDbContext** - General application data
- **WeatherDbContext** - Weather telemetry and history
- Separation allows independent scaling and backup strategies

### Repository Pattern
Abstraction over EF Core for:
- Testability (easy mocking)
- Consistent data access patterns
- Query encapsulation
- Transaction management

### Entity Framework Features
- **Fluent API Configuration** - Type-safe entity configuration
- **Change Tracking** - Automatic dirty detection
- **Migrations** - Database schema versioning
- **Query Optimization** - Compiled queries for performance

## 🗄️ Database Schema

### Weather Tables
```sql
CREATE TABLE WeatherDataPoints (
    Id INTEGER PRIMARY KEY,
    Timestamp DATETIME NOT NULL,
    Temperature REAL,
    Humidity REAL,
    Pressure REAL,
    WindSpeed REAL,
    WindDirection REAL,
    DewPoint REAL,
    Created DATETIME NOT NULL
);
```

### Equipment Tables
```sql
CREATE TABLE EquipmentStatus (
    Id INTEGER PRIMARY KEY,
    EquipmentId TEXT NOT NULL,
    Status TEXT NOT NULL,
    Timestamp DATETIME NOT NULL,
    Details TEXT
);
```

## ⚙️ Configuration

### Connection Strings

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "ApplicationDb": "Data Source=hvo-app.db",
    "WeatherDb": "Data Source=weather-history.db"
  }
}
```

### Dependency Injection Setup

```csharp
// Startup.cs or Program.cs
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(
        configuration.GetConnectionString("ApplicationDb"),
        b => b.MigrationsAssembly("HVO.DataModels")
    ));

services.AddDbContext<WeatherDbContext>(options =>
    options.UseSqlite(configuration.GetConnectionString("WeatherDb")));

// Register repositories
services.AddScoped<IWeatherRepository, WeatherRepository>();
```

Or use the extension method:
```csharp
services.AddHVODataModels(configuration);
```

## 🎓 Usage Examples

### Repository Pattern Usage

```csharp
public class WeatherService
{
    private readonly IWeatherRepository _repository;
    
    public WeatherService(IWeatherRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<Result<WeatherDataPoint>> GetLatestWeatherAsync()
    {
        try
        {
            var latest = await _repository.GetLatestAsync();
            return latest != null
                ? Result<WeatherDataPoint>.Success(latest)
                : Result<WeatherDataPoint>.Failure(
                    new InvalidOperationException("No weather data available"));
        }
        catch (Exception ex)
        {
            return Result<WeatherDataPoint>.Failure(ex);
        }
    }
    
    public async Task<Result<List<WeatherDataPoint>>> GetHistoryAsync(
        DateTime start, 
        DateTime end)
    {
        try
        {
            var data = await _repository.GetRangeAsync(start, end);
            return Result<List<WeatherDataPoint>>.Success(data.ToList());
        }
        catch (Exception ex)
        {
            return Result<List<WeatherDataPoint>>.Failure(ex);
        }
    }
}
```

### Direct DbContext Usage

```csharp
public class EquipmentMonitor
{
    private readonly ApplicationDbContext _context;
    
    public async Task LogStatusAsync(string equipmentId, string status)
    {
        var entry = new EquipmentStatus
        {
            EquipmentId = equipmentId,
            Status = status,
            Timestamp = DateTime.UtcNow
        };
        
        _context.EquipmentStatus.Add(entry);
        await _context.SaveChangesAsync();
    }
    
    public async Task<List<EquipmentStatus>> GetRecentStatusAsync(
        string equipmentId, 
        int count = 100)
    {
        return await _context.EquipmentStatus
            .Where(e => e.EquipmentId == equipmentId)
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync();
    }
}
```

## 🗂️ Database Migrations

### Create Migration
```bash
# From src/HVO.DataModels/
dotnet ef migrations add InitialCreate --context ApplicationDbContext

# For weather DB
dotnet ef migrations add WeatherSchema --context WeatherDbContext
```

### Apply Migration
```bash
dotnet ef database update --context ApplicationDbContext
dotnet ef database update --context WeatherDbContext
```

### Production Migrations
Applications should apply migrations at startup:
```csharp
public static async Task Main(string[] args)
{
    var app = builder.Build();
    
    using (var scope = app.Services.CreateScope())
    {
        var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var weatherDb = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
        
        await appDb.Database.MigrateAsync();
        await weatherDb.Database.MigrateAsync();
    }
    
    await app.RunAsync();
}
```

## 📊 Performance Considerations

### Compiled Queries
For frequently-used queries:
```csharp
private static readonly Func<WeatherDbContext, DateTime, Task<WeatherDataPoint?>> 
    GetLatestQuery = EF.CompileAsyncQuery(
        (WeatherDbContext ctx, DateTime since) =>
            ctx.WeatherDataPoints
                .Where(w => w.Timestamp >= since)
                .OrderByDescending(w => w.Timestamp)
                .FirstOrDefault());
```

### Indexing
Create indexes in fluent configuration:
```csharp
entity.HasIndex(e => e.Timestamp)
    .HasDatabaseName("IX_WeatherDataPoints_Timestamp");
    
entity.HasIndex(e => new { e.EquipmentId, e.Timestamp })
    .HasDatabaseName("IX_EquipmentStatus_EquipmentId_Timestamp");
```

### Projection
Use `Select()` to fetch only needed columns:
```csharp
var summary = await _context.WeatherDataPoints
    .Where(w => w.Timestamp >= startDate)
    .Select(w => new WeatherSummary 
    { 
        Timestamp = w.Timestamp, 
        Temperature = w.Temperature 
    })
    .ToListAsync();
```

## 🧪 Testing

### In-Memory Database for Tests
```csharp
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(databaseName: "TestDb")
    .Options;

using var context = new ApplicationDbContext(options);

// Seed test data
context.WeatherDataPoints.Add(new WeatherDataPoint 
{ 
    Temperature = 20.5, 
    Timestamp = DateTime.UtcNow 
});
await context.SaveChangesAsync();

// Test repository
var repository = new WeatherRepository(context);
var latest = await repository.GetLatestAsync();
Assert.NotNull(latest);
```

## 🔗 Dependencies

- **Microsoft.EntityFrameworkCore** - EF Core runtime
- **Microsoft.EntityFrameworkCore.Sqlite** - SQLite provider
- **Microsoft.EntityFrameworkCore.Tools** - Migration tools
- **HVO** - Core library for Result<T> pattern

## 📚 Used By

- `HVO.WebSite.v9` - Main website data access
- `HVO.WebSite.Playground` - Test website
- `HVO.SkyMonitorV5.RPi` - Sky monitoring data (may migrate to separate context)
- `HVO.RoofControllerV4.RPi` - Equipment status logging

## 🔄 Future Enhancements

- [ ] Add TimescaleDB support for time-series optimization
- [ ] Implement read replicas for query scaling
- [ ] Add audit logging interceptor
- [ ] Create `DbContext` pooling for high-throughput scenarios
- [ ] Separate context for sky monitor archives (large binary data)

## 📖 Related Documentation

- [EF Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [Repository Pattern](https://learn.microsoft.com/en-us/previous-versions/msp-n-p/ff649690(v=pandp.10))
- [HVOv9 Database Schema](../../docs/database-schema.md) *(if exists)*
