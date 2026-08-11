using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public sealed class DavisArchiveCursorStoreTests
{
    [TestMethod]
    public async Task Cursor_PersistsIndependentTimestampsAndAdvancesMonotonically()
    {
        var root = Path.Combine(Path.GetTempPath(), $"davis-cursor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<DavisLocalDbContext>(builder => builder.UseSqlite($"Data Source={Path.Combine(root, "local.db")}"));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<DavisArchiveCursorStore>();
            await using var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await DavisLocalDatabaseInitializer.EnsureCreatedAsync(scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>());
            var store = provider.GetRequiredService<DavisArchiveCursorStore>();
            var local = new DateTime(2026, 8, 11, 5, 0, 0, DateTimeKind.Unspecified);
            var utc = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

            (await store.AdvanceAsync("station-1", local, utc, CancellationToken.None)).Should().BeTrue();
            (await store.AdvanceAsync("station-1", local.AddMinutes(-5), utc.AddMinutes(5), CancellationToken.None)).Should().BeFalse();

            var cursor = await store.GetAsync("station-1", CancellationToken.None);
            cursor.Should().NotBeNull();
            cursor!.ConsoleRecordedAtLocal.Should().Be(local);
            cursor.ConsoleRecordedAtLocal.Kind.Should().Be(DateTimeKind.Unspecified);
            cursor.RecordedAtUtc.Should().Be(utc);
            cursor.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
