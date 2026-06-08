using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaDeviceProfileCatalogTests
{
    [TestMethod]
    [DataRow("ep25-sysinfo.json", "ep25", KasaDeviceKind.Plug, typeof(IKasaSwitchModule), typeof(IKasaEnergyModule), typeof(IKasaScheduleModule))]
    [DataRow("hs105-sysinfo.json", "hs105", KasaDeviceKind.Plug, typeof(IKasaSwitchModule), typeof(IKasaScheduleModule), null)]
    [DataRow("hs200-sysinfo.json", "hs200", KasaDeviceKind.Switch, typeof(IKasaSwitchModule), typeof(IKasaScheduleModule), null)]
    [DataRow("hs210-sysinfo.json", "hs210", KasaDeviceKind.ThreeWaySwitch, typeof(IKasaSwitchModule), typeof(IKasaScheduleModule), null)]
    [DataRow("hs220-sysinfo.json", "hs220", KasaDeviceKind.Dimmer, typeof(IKasaSwitchModule), typeof(IKasaDimmerModule), null)]
    [DataRow("hs300-sysinfo.json", "hs300", KasaDeviceKind.PowerStrip, typeof(IKasaChildOutletModule), typeof(IKasaEnergyModule), typeof(IKasaScheduleModule))]
    [DataRow("kp200-sysinfo.json", "kp200", KasaDeviceKind.DualOutlet, typeof(IKasaChildOutletModule), typeof(IKasaScheduleModule), null)]
    [DataRow("kl130-sysinfo.json", "kl130", KasaDeviceKind.Bulb, typeof(IKasaLightModule), typeof(IKasaEnergyModule), typeof(IKasaScheduleModule))]
    [DataRow("lb230-sysinfo.json", "lb230", KasaDeviceKind.Bulb, typeof(IKasaLightModule), typeof(IKasaEnergyModule), typeof(IKasaScheduleModule))]
    public void Resolve_KnownFixtures_ReturnsConcreteProfileWithExpectedModules(string fixture, string profileId, KasaDeviceKind kind, Type firstModule, Type secondModule, Type? thirdModule)
    {
        var systemInfo = ParseFixture(fixture);

        var definition = KasaDeviceProfileCatalog.Resolve(systemInfo);

        definition.ProfileId.Should().Be(profileId);
        definition.DeviceKind.Should().Be(kind);
        definition.Modules.Should().Contain(module => firstModule.IsInstanceOfType(module));
        definition.Modules.Should().Contain(module => secondModule.IsInstanceOfType(module));
        if (thirdModule is not null)
        {
            definition.Modules.Should().Contain(module => thirdModule.IsInstanceOfType(module));
        }
    }

    [TestMethod]
    public void Resolve_Kp200Profile_ModelsChildScheduleWithoutEnergy()
    {
        var definition = KasaDeviceProfileCatalog.Resolve(ParseFixture("kp200-sysinfo.json"));

        var childOutlets = definition.Modules.OfType<IKasaChildOutletModule>().Should().ContainSingle().Subject;
        childOutlets.ExpectedOutletCount.Should().Be(2);
        definition.Modules.OfType<IKasaEnergyModule>().Should().BeEmpty();
        var schedule = definition.Modules.OfType<IKasaScheduleModule>().Should().ContainSingle().Subject;
        schedule.IsChildScoped.Should().BeTrue();
        schedule.SupportsCountdown.Should().BeTrue();
        schedule.SupportsAwayMode.Should().BeTrue();
    }

    [TestMethod]
    public void Resolve_Hs300Profile_ModelsChildOutletsAndChildEnergy()
    {
        var definition = KasaDeviceProfileCatalog.Resolve(ParseFixture("hs300-sysinfo.json"));

        definition.Modules.OfType<IKasaChildOutletModule>().Should().ContainSingle().Which.ExpectedOutletCount.Should().Be(6);
        var energy = definition.Modules.OfType<IKasaEnergyModule>().Should().ContainSingle().Subject;
        energy.Scope.Should().Be("child");
        energy.HasRealtimePower.Should().BeTrue();
        energy.HasVoltage.Should().BeTrue();
        energy.HasCurrent.Should().BeTrue();
        energy.HasTotalEnergy.Should().BeTrue();
        energy.HasHistory.Should().BeTrue();
    }

    [TestMethod]
    public void Resolve_Kl130Profile_ModelsPowerOnlyBulbEnergy()
    {
        var definition = KasaDeviceProfileCatalog.Resolve(ParseFixture("kl130-sysinfo.json"));

        var light = definition.Modules.OfType<IKasaLightModule>().Should().ContainSingle().Subject;
        light.SupportsDimming.Should().BeTrue();
        light.SupportsColor.Should().BeTrue();
        light.SupportsColorTemperature.Should().BeTrue();
        light.HasPreferredStates.Should().BeTrue();
        var energy = definition.Modules.OfType<IKasaEnergyModule>().Should().ContainSingle().Subject;
        energy.HasRealtimePower.Should().BeTrue();
        energy.HasVoltage.Should().BeFalse();
        energy.HasCurrent.Should().BeFalse();
        energy.HasTotalEnergy.Should().BeFalse();
        energy.HasHistory.Should().BeFalse();
    }

    private static KasaSystemInfo ParseFixture(string fixture)
    {
        if (fixture == "hs105-sysinfo.json")
        {
            using var hs105Document = JsonDocument.Parse("""
                {"system":{"get_sysinfo":{"err_code":0,"sw_ver":"1.5.6","hw_ver":"1.0","model":"HS105(US)","deviceId":"HS105_DEVICE_ID_SANITIZED","alias":"Sanitized HS105","mac":"AA:BB:CC:DD:EE:09","relay_state":1,"on_time":12}}}
                """);
            return new KasaSystemInfoParser().Parse(hs105Document);
        }

        using var document = JsonDocument.Parse(FixtureLoader.Read(fixture));
        return new KasaSystemInfoParser().Parse(document);
    }
}
