using FluentAssertions;
using System.Text.Json;
using HVO.Edge.Outbox;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Models;
using HVO.Gateway.TplinkKasa.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Gateway.TplinkKasa.Tests.Outbox;

[TestClass]
public sealed class KasaOutboxWriterTests
{
    [TestMethod]
    public async Task EnqueueEnergyAsync_InsertsRecord_WhenValid()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var writer = fixture.CreateWriter();
        var snapshot = CreateSnapshot(energy: new KasaEnergyReading(12.3, 119.8, 0.1, 2.5, default));

        var inserted = await writer.EnqueueEnergyAsync(snapshot, CancellationToken.None);

        inserted.Should().BeTrue();
        var record = await fixture.Context.OutboxRecords.SingleAsync();
        record.PayloadType.Should().Be(KasaOutboxPayloadTypes.Energy);
        record.PayloadVersion.Should().Be(KasaOutboxPayloadTypes.EnergyVersion);
        record.SourceId.Should().Be(snapshot.SourceId);
        record.DeviceId.Should().Be(snapshot.DeviceId);

        var payload = JsonSerializer.Deserialize<KasaEnergyPayload>(record.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotBeNull();
        payload!.LoadPowerW.Should().Be(12.3);
        payload.GridVoltageV.Should().Be(119.8);
        record.PayloadJson.Should().NotContain("powerW");
        record.PayloadJson.Should().NotContain("voltageV");
    }

    [TestMethod]
    public async Task EnqueueEnergyAsync_InsertsOutletRecords_WhenDeviceEnergyIsNull()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var writer = fixture.CreateWriter();
        var snapshot = CreateSnapshot(
            energy: null,
            outlets:
            [
                new KasaOutletSnapshot("outlet-a", 1, "Outlet A", true, 10, new KasaEnergyReading(7.5, 120.1, 0.06, 1.1, default))
            ]);

        var inserted = await writer.EnqueueEnergyAsync(snapshot, CancellationToken.None);

        inserted.Should().BeTrue();
        var record = await fixture.Context.OutboxRecords.SingleAsync();
        record.SourceId.Should().Be("tplink-kasa:desk-lamp:outlet:outlet-a");
        record.DeviceId.Should().Be("outlet-a");
    }

    [TestMethod]
    public async Task EnqueueEnergyAsync_SkipsDuplicate()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var writer = fixture.CreateWriter();
        var snapshot = CreateSnapshot(energy: new KasaEnergyReading(12.3, 119.8, 0.1, 2.5, default));

        var first = await writer.EnqueueEnergyAsync(snapshot, CancellationToken.None);
        var second = await writer.EnqueueEnergyAsync(snapshot, CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await fixture.Context.OutboxRecords.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task EnqueueEnergyAsync_SkipsNullEnergy()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var writer = fixture.CreateWriter();
        var snapshot = CreateSnapshot(energy: null);

        var inserted = await writer.EnqueueEnergyAsync(snapshot, CancellationToken.None);

        inserted.Should().BeFalse();
        (await fixture.Context.OutboxRecords.CountAsync()).Should().Be(0);
    }

    private static KasaDeviceSnapshot CreateSnapshot(
        KasaEnergyReading? energy,
        IReadOnlyList<KasaOutletSnapshot>? outlets = null) => new(
        DeviceId: "vendor-device-id",
        SourceId: "tplink-kasa:desk-lamp",
        Host: "192.0.2.10",
        ObservedAtUtc: new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero),
        IsOnline: true,
        IdentityValidated: true,
        IdentityMismatchReason: null,
        Alias: "Desk Lamp",
        Model: "KP115(US)",
        HardwareVersion: "1.0",
        SoftwareVersion: "1.2.3",
        MacAddress: "00:11:22:33:44:55",
        DeviceKind: KasaDeviceKind.Plug,
        Capabilities: new HashSet<KasaCapability> { KasaCapability.EnergyRealtime },
        MetadataCapabilities: new HashSet<KasaMetadataCapability>(),
        CommandCapabilities: new HashSet<KasaCommandCapability>(),
        IsOn: true,
        Outlets: outlets ?? [],
        Light: null,
        Energy: energy,
        DeviceInfo: new KasaDeviceInfo(
            DeviceType: "IOT.SMARTPLUGSWITCH",
            Model: "KP115(US)",
            HardwareVersion: "1.0",
            SoftwareVersion: "1.2.3",
            MacAddress: "00:11:22:33:44:55",
            HardwareId: "hardware-id",
            FirmwareId: "firmware-id",
            OemId: "oem-id",
            Feature: null,
            ActiveMode: null,
            Rssi: null,
            Location: null,
            DeviceTime: null,
            Timezone: null,
            DeviceUtcOffsetMinutes: null,
            DeviceTimeZoneLabel: null,
            Cloud: null,
            FirmwareDownload: null,
            CloudFirmware: null,
            Dimmer: null),
        ReadMetadata: null,
        RawSystemInfo: default);

    private sealed class OutboxFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private OutboxFixture(SqliteConnection connection, OutboxDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public OutboxDbContext Context { get; }

        public static async Task<OutboxFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<OutboxDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new OutboxDbContext(options);
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                context,
                KasaOutboxPayloadTypes.Energy,
                KasaOutboxPayloadTypes.EnergyVersion);
            return new OutboxFixture(connection, context);
        }

        public KasaOutboxWriter CreateWriter() => new(
            new EdgeOutboxStore<OutboxDbContext>(Context),
            NullLogger<KasaOutboxWriter>.Instance);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
