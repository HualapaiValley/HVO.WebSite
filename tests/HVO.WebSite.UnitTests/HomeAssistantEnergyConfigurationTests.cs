using FluentAssertions;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HomeAssistantEnergyConfigurationTests
{
    private static string ConfigurationRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "Configuration");

    [TestMethod]
    public void EnergyHelpers_PreserveAvailabilityAndUseNonOverlappingNumericalSums()
    {
        var templates = File.ReadAllText(Path.Combine(ConfigurationRoot, "templates", "hvo.yaml"));
        var integrations = File.ReadAllText(Path.Combine(ConfigurationRoot, "sensors", "hvo-energy.yaml"));

        templates.Should().Contain("| has_value")
            .And.Contain("name: HVO All PV Power")
            .And.Contain("name: HVO Total PV Energy Daily")
            .And.Contain("HVO SmartShunt Battery Charge Power")
            .And.Contain("HVO SmartShunt Battery Discharge Power");
        templates.Should().ContainAll(
            "states('sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f1_x5fpower') | float",
            "states('sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f2_x5fpower') | float",
            "states('sensor.hvo_3xhvo_3xeg4_15xcontroller_x2da_24xpv_x5fmppt_x5f1_x5fpower') | float",
            "states('sensor.hvo_6500ex_pv_energy_daily') | float",
            "states('sensor.hvo_external_mppt_pv_energy_daily') | float");
        integrations.Should().Contain("HVO External MPPT PV Energy")
            .And.Contain("HVO SmartShunt Battery Charge Energy")
            .And.Contain("HVO SmartShunt Battery Discharge Energy")
            .And.Contain("unit_prefix: k")
            .And.Contain("max_sub_interval:");
    }

    [TestMethod]
    public void EnergyDashboard_HasEightPurposeBuiltViewsAndDetailedReadOnlyInstruments()
    {
        var dashboard = File.ReadAllText(Path.Combine(ConfigurationRoot, "dashboards", "hvo-energy.yaml"));

        dashboard.Split("  - title:", StringSplitOptions.None).Should().HaveCount(9);
        dashboard.Should().ContainAll(
                "title: Overview", "title: Generation", "title: Usage", "title: Batteries",
                "title: 6500EX", "title: MPPT100", "title: SmartShunt", "title: JK Banks And Health")
            .And.Contain("name: 6500EX MPPT 1")
            .And.Contain("name: 6500EX MPPT 2")
            .And.Contain("name: Standalone MPPT100")
            .And.Contain("name: 6500EX Native Subtotal")
            .And.Contain("name: All-Three Calculated Total")
            .And.Contain("type: gauge")
            .And.Contain("type: statistics-graph")
            .And.Contain("type: conditional");
        Enumerable.Range(1, 7).Should().OnlyContain(bank => dashboard.Contains($"title: Bank {bank}", StringComparison.Ordinal));
        dashboard.Should().NotContain("grid_energy")
            .And.NotContain("Grid Import")
            .And.NotContain("Grid Export");
    }

    [TestMethod]
    public void EnergyDashboard_UsesOnlySelectedPhysicalKasaParents()
    {
        var dashboard = File.ReadAllText(Path.Combine(ConfigurationRoot, "dashboards", "hvo-energy.yaml"));

        dashboard.Should().ContainAll(
            "sensor.tp_link_power_strip_4540_current_consumption",
            "sensor.tp_link_power_strip_0628_current_consumption",
            "sensor.workshop_power_strip_plug_1_current_consumption",
            "sensor.tp_link_power_strip_d65c_current_consumption",
            "sensor.power_room_heater_current_consumption",
            "sensor.tpra_smart_switch_current_consumption");
        dashboard.Should().NotContain("workshop_mr_cool_ac_current_consumption");
    }

    [TestMethod]
    public void AggregateHelpers_GuardAndSumExactlyTheirPhysicalOperands()
    {
        var templates = File.ReadAllText(Path.Combine(ConfigurationRoot, "templates", "hvo.yaml"));
        var pvBlock = SensorBlock(templates, "HVO All PV Power");
        var dailyBlock = SensorBlock(templates, "HVO Total PV Energy Daily");
        var pvEntities = StateOperands(pvBlock);
        var dailyEntities = StateOperands(dailyBlock);

        pvEntities.Should().Equal(
            "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f1_x5fpower",
            "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f2_x5fpower",
            "sensor.hvo_3xhvo_3xeg4_15xcontroller_x2da_24xpv_x5fmppt_x5f1_x5fpower");
        dailyEntities.Should().Equal("sensor.hvo_6500ex_pv_energy_daily", "sensor.hvo_external_mppt_pv_energy_daily");
        pvEntities.Should().OnlyContain(entity => Regex.Matches(pvBlock, Regex.Escape(entity)).Count == 2,
            "each PV operand must appear once in availability and once in state");
        dailyEntities.Should().OnlyContain(entity => Regex.Matches(dailyBlock, Regex.Escape(entity)).Count == 2,
            "each daily operand must appear once in availability and once in state");

        var samples = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [pvEntities[0]] = 1250.25,
            [pvEntities[1]] = 980.5,
            [pvEntities[2]] = 475.75,
            [dailyEntities[0]] = 18.125,
            [dailyEntities[1]] = 4.375,
        };
        pvEntities.Sum(entity => samples[entity]).Should().Be(2706.5);
        dailyEntities.Sum(entity => samples[entity]).Should().Be(22.5);
    }

    [TestMethod]
    public void UtilityMeters_DeriveDailyValuesFromCumulativeCounters()
    {
        var meters = File.ReadAllText(Path.Combine(ConfigurationRoot, "utility-meters", "hvo-energy.yaml"));

        meters.Should().Contain("hvo_6500ex_pv_energy_daily:")
            .And.Contain("hvo_external_mppt_pv_energy_daily:")
            .And.Contain("hvo_battery_charge_energy_daily:")
            .And.Contain("hvo_battery_discharge_energy_daily:");
        meters.Split("cycle: daily", StringSplitOptions.None).Should().HaveCount(6);
        meters.Split("periodically_resetting: false", StringSplitOptions.None).Should().HaveCount(6);
        meters.Should().NotContain("always_available");
        foreach (var meterId in new[]
                 {
                     "hvo_6500ex_pv_energy_daily",
                     "hvo_6500ex_ac_load_energy_daily",
                     "hvo_external_mppt_pv_energy_daily",
                     "hvo_battery_charge_energy_daily",
                     "hvo_battery_discharge_energy_daily",
                 })
        {
            var block = Regex.Match(
                meters,
                $@"(?m)^{Regex.Escape(meterId)}:\r?\n(?<body>(?:^  [^\r\n]*(?:\r?\n|$))*)");
            block.Success.Should().BeTrue($"{meterId} must be configured");
            block.Groups["body"].Value.Should().Contain("periodically_resetting: false");
        }
    }

    [TestMethod]
    public async Task PostRestartValidator_AcceptsConsistentNumericAndUnavailableSourceStates()
    {
        var numeric = EnergyStates("600", "3.25");
        var unavailable = EnergyStates("unavailable", "3.25", firstPowerSource: "unavailable");

        var numericResult = await RunEnergyStateValidatorAsync(numeric);
        var unavailableResult = await RunEnergyStateValidatorAsync(unavailable);

        numericResult.ExitCode.Should().Be(0, numericResult.Error);
        unavailableResult.ExitCode.Should().Be(0, unavailableResult.Error);
    }

    [TestMethod]
    public async Task PostRestartValidator_RejectsMetadataAndArithmeticDrift()
    {
        var wrongMetadata = EnergyStates("600", "3.25", powerUnit: "kW");
        var wrongArithmetic = EnergyStates("605", "3.25");

        var metadataResult = await RunEnergyStateValidatorAsync(wrongMetadata);
        var arithmeticResult = await RunEnergyStateValidatorAsync(wrongArithmetic);

        metadataResult.ExitCode.Should().NotBe(0);
        arithmeticResult.ExitCode.Should().NotBe(0);
        metadataResult.Error.Should().Contain("missing, have invalid metadata, or disagree");
        arithmeticResult.Error.Should().Contain("missing, have invalid metadata, or disagree");
    }

    [TestMethod]
    public async Task PostRestartValidator_RejectsStaleNumericHelperWhenSourceIsUnavailable()
    {
        var stale = EnergyStates("600", "3.25", firstPowerSource: "unavailable");

        var result = await RunEnergyStateValidatorAsync(stale);

        result.ExitCode.Should().NotBe(0);
        result.Error.Should().Contain("missing, have invalid metadata, or disagree");
    }

    [TestMethod]
    public void DashboardDeployment_ValidatesRestartedHelperStatesBeforeDeletingBackup()
    {
        var script = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "deploy-home-assistant-dashboard.sh"));
        var stateFetch = script.IndexOf("post_restart_states=", StringComparison.Ordinal);
        var helperValidation = script.IndexOf("validate-home-assistant-energy-state.sh", StringComparison.Ordinal);
        var backupDeletion = script.IndexOf("rm -rf '$backup_root'", helperValidation, StringComparison.Ordinal);

        stateFetch.Should().BeGreaterThan(0);
        helperValidation.Should().BeGreaterThan(stateFetch);
        backupDeletion.Should().BeGreaterThan(helperValidation);
        script[helperValidation..backupDeletion].Should().Contain("rollback");
    }

    private static string SensorBlock(string templates, string name)
    {
        var start = templates.IndexOf($"    - name: {name}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = templates.IndexOf("    - name:", start + 1, StringComparison.Ordinal);
        return templates[start..(end < 0 ? templates.Length : end)];
    }

    private static string[] StateOperands(string block) => Regex.Matches(
            block,
            @"states\('([^']+)'\) \| float")
        .Select(match => match.Groups[1].Value)
        .ToArray();

    private static object[] EnergyStates(
        string powerHelper,
        string dailyHelper,
        string firstPowerSource = "100",
        string powerUnit = "W") =>
    [
        State("sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f1_x5fpower", firstPowerSource, "W", "power", "measurement"),
        State("sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f2_x5fpower", "200", "W", "power", "measurement"),
        State("sensor.hvo_3xhvo_3xeg4_15xcontroller_x2da_24xpv_x5fmppt_x5f1_x5fpower", "300", "W", "power", "measurement"),
        State("sensor.hvo_all_pv_power", powerHelper, powerUnit, "power", "measurement"),
        State("sensor.hvo_6500ex_pv_energy_daily", "2.0", "kWh", "energy", "total_increasing"),
        State("sensor.hvo_external_mppt_pv_energy_daily", "1.25", "kWh", "energy", "total_increasing"),
        State("sensor.hvo_total_pv_energy_daily", dailyHelper, "kWh", "energy", "total_increasing"),
    ];

    private static object State(string entityId, string state, string unit, string deviceClass, string stateClass) => new
    {
        entity_id = entityId,
        state,
        attributes = new { unit_of_measurement = unit, device_class = deviceClass, state_class = stateClass },
    };

    private static async Task<ScriptResult> RunEnergyStateValidatorAsync(object[] states)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "validate-home-assistant-energy-state.sh");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add(script);
        process.Start();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(states));
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ScriptResult(process.ExitCode, output, error);
    }

    private sealed record ScriptResult(int ExitCode, string Output, string Error);
}
