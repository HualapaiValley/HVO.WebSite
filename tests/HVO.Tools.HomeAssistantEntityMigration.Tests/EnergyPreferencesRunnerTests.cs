using System.Text.Json;
using FluentAssertions;

namespace HVO.Tools.HomeAssistantEntityMigration.Tests;

[TestClass]
public sealed class EnergyPreferencesRunnerTests
{
    [TestMethod]
    public void Validation_EmptyArraysForEveryCategory_Succeeds()
    {
        var validation = ValidEnergyValidation();

        var act = () => EnergyPreferencesRunner.EnsureEnergyValidationSucceeded(validation);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Validation_DeviceConsumptionError_IsRejectedAndDiagnosable()
    {
        var validation = InvalidEnergyValidation();

        var act = () => EnergyPreferencesRunner.EnsureEnergyValidationSucceeded(validation);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*device_consumption*entity_unavailable*");
    }

    [TestMethod]
    public async Task Apply_PreservesUnrelatedEntriesAndReplacesLegacyManagedConsumption()
    {
        var manifest = await LoadManifestAsync();
        var previous = PreviousPreferences(manifest);
        await using var client = FakeClient.For(manifest, previous);
        var runner = new EnergyPreferencesRunner(client, manifest);

        await runner.ApplyAsync(CancellationToken.None);

        client.SaveRequests.Should().ContainSingle();
        var actual = client.StoredPreferences!.Value;
        Names(actual, "energy_sources").Should().BeEquivalentTo(
            "Unrelated Solar", "HVO 6500EX Solar", "HVO External MPPT Solar", "HVO Battery");
        Names(actual, "device_consumption").Should().BeEquivalentTo(
            "Unrelated Device", "HVO 6500EX AC Load", "Control Room Strip", "Telescope Strip",
            "Workshop Strip", "Observatory Amenities Strip", "Power Room Heater", "TPRA Workshop Strip");
        Entries(actual, "device_consumption").Any(entry =>
            entry.TryGetProperty("stat_consumption", out var entity)
            && entity.GetString() == "sensor.workshop_power_strip_plug_1_workshop_mr_cool_ac_today_s_consumption")
            .Should().BeFalse();
        Entries(actual, "device_consumption").Where(entry => entry.TryGetProperty("included_in_stat", out _))
            .Should().OnlyContain(entry => entry.GetProperty("included_in_stat").GetString() ==
                "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_23xload_x5fenergy_x5ftotal");
        Entries(actual, "device_consumption_water").Should().ContainSingle()
            .Which.GetProperty("name").GetString().Should().Be("Unrelated Water");
    }

    [TestMethod]
    public async Task Apply_SaveFailure_RestoresAndReadsBackPreviousPreferences()
    {
        var manifest = await LoadManifestAsync();
        var previous = PreviousPreferences(manifest);
        await using var client = FakeClient.For(manifest, previous);
        client.SaveOutcomes.Enqueue(new InvalidOperationException("save failed"));
        client.SaveOutcomes.Enqueue(null);
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.ApplyAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("save failed");
        client.SaveRequests.Should().HaveCount(2);
        Json(client.StoredPreferences!.Value).Should().Be(Json(previous));
        client.PreferenceReadCount.Should().Be(2, "rollback must read back the restored preferences");
    }

    [TestMethod]
    public async Task Apply_CanceledAfterMutatingSave_UsesIndependentTokenToRestoreAndVerifyPreviousPreferences()
    {
        var manifest = await LoadManifestAsync();
        var previous = PreviousPreferences(manifest);
        using var operationCancellation = new CancellationTokenSource();
        await using var client = FakeClient.For(manifest, previous);
        client.CancelAfterFirstMutatingSave = operationCancellation;
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.ApplyAsync(operationCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        operationCancellation.IsCancellationRequested.Should().BeTrue();
        client.SaveRequests.Should().HaveCount(2);
        client.SaveTokens.Should().HaveCount(2);
        client.SaveTokens[1].CanBeCanceled.Should().BeTrue();
        client.SaveTokens[1].IsCancellationRequested.Should().BeFalse();
        client.SaveTokens[1].Should().NotBe(operationCancellation.Token);
        client.PreferenceReadTokens.Should().HaveCount(2);
        client.PreferenceReadTokens[1].IsCancellationRequested.Should().BeFalse();
        client.PreferenceReadTokens[1].Should().Be(client.SaveTokens[1]);
        Json(client.StoredPreferences!.Value).Should().Be(Json(previous));
    }

    [TestMethod]
    public async Task Apply_DesiredReadbackMismatch_RollsBackAndVerifiesPreviousPreferences()
    {
        var manifest = await LoadManifestAsync();
        var previous = PreviousPreferences(manifest);
        await using var client = FakeClient.For(manifest, previous);
        client.PreferenceReads.Enqueue(previous);
        client.PreferenceReads.Enqueue(EmptyPreferences());
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.ApplyAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*do not match*");
        client.SaveRequests.Should().HaveCount(2);
        Json(client.StoredPreferences!.Value).Should().Be(Json(previous));
        client.PreferenceReadCount.Should().Be(3);
    }

    [TestMethod]
    public async Task Apply_EnergyValidationFailure_RollsBackAndVerifiesPreviousPreferences()
    {
        var manifest = await LoadManifestAsync();
        var previous = PreviousPreferences(manifest);
        await using var client = FakeClient.For(manifest, previous);
        client.EnergyValidations.Enqueue(InvalidEnergyValidation());
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.ApplyAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*device_consumption*");
        client.SaveRequests.Should().HaveCount(2);
        Json(client.StoredPreferences!.Value).Should().Be(Json(previous));
        client.PreferenceReadCount.Should().Be(3);
    }

    [TestMethod]
    public async Task Apply_RollbackReadbackMismatch_PreservesBothFailures()
    {
        var manifest = await LoadManifestAsync();
        var previous = PreviousPreferences(manifest);
        await using var client = FakeClient.For(manifest, previous);
        client.PreferenceReads.Enqueue(previous);
        client.PreferenceReads.Enqueue(EmptyPreferences());
        client.PreferenceReads.Enqueue(EmptyPreferences());
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.ApplyAsync(CancellationToken.None);

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
        exception.Which.InnerExceptions[0].Message.Should().Contain("do not match");
        exception.Which.InnerExceptions[1].Message.Should().Contain("rollback did not match");
    }

    [TestMethod]
    public async Task Check_UnavailableManagedRateAndSoc_AreAcceptedWhenMetadataAndStatisticsAreValid()
    {
        var manifest = await LoadManifestAsync();
        await using var client = FakeClient.For(manifest, ManagedPreferences(manifest));
        var rate = manifest.EnergySources[0].GetProperty("stat_rate").GetString()!;
        var soc = manifest.EnergySources.Single(source => source.GetProperty("type").GetString() == "battery")
            .GetProperty("stat_soc").GetString()!;
        var cumulative = manifest.EnergySources[0].GetProperty("stat_energy_from").GetString()!;
        client.States[rate] = State(rate, "unavailable", "power", "measurement", "W");
        client.States[soc] = State(soc, "unknown", "battery", "measurement", "%");
        client.States[cumulative] = State(cumulative, "unavailable", "energy", "total_increasing", "kWh");
        var runner = new EnergyPreferencesRunner(client, manifest);

        await runner.CheckAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Check_MalformedManagedRateState_IsRejected()
    {
        var manifest = await LoadManifestAsync();
        await using var client = FakeClient.For(manifest, ManagedPreferences(manifest));
        var rate = manifest.EnergySources[0].GetProperty("stat_rate").GetString()!;
        client.States[rate] = State(rate, "sleeping", "power", "measurement", "W");
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.CheckAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*malformed current state*" + rate + "*");
    }

    [TestMethod]
    public async Task Check_NumericSocOutsidePercentageRange_IsRejected()
    {
        var manifest = await LoadManifestAsync();
        await using var client = FakeClient.For(manifest, ManagedPreferences(manifest));
        var soc = manifest.EnergySources.Single(source => source.GetProperty("type").GetString() == "battery")
            .GetProperty("stat_soc").GetString()!;
        client.States[soc] = State(soc, "101", "battery", "measurement", "%");
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.CheckAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside 0-100*" + soc + "*");
    }

    [TestMethod]
    public async Task Check_InvalidManagedRateMetadata_IsRejected()
    {
        var manifest = await LoadManifestAsync();
        await using var client = FakeClient.For(manifest, ManagedPreferences(manifest));
        var rate = manifest.EnergySources[0].GetProperty("stat_rate").GetString()!;
        client.States[rate] = State(rate, "10", "energy", "measurement", "W");
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.CheckAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*device_class=power*");
    }

    [TestMethod]
    public async Task Check_ManagedEntityWithoutEligibleRecorderStatistic_IsRejected()
    {
        var manifest = await LoadManifestAsync();
        await using var client = FakeClient.For(manifest, ManagedPreferences(manifest));
        var consumption = manifest.DeviceConsumption[0].GetProperty("stat_consumption").GetString()!;
        client.Statistics.Remove(consumption);
        var runner = new EnergyPreferencesRunner(client, manifest);

        var act = () => runner.CheckAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no recorder statistics metadata*" + consumption + "*");
    }

    private static Task<EnergyPreferencesManifest> LoadManifestAsync() =>
        EnergyPreferencesManifest.LoadAsync(Path.Combine(AppContext.BaseDirectory, "hvo-energy-preferences.json"), CancellationToken.None);

    private static JsonElement PreviousPreferences(EnergyPreferencesManifest manifest)
    {
        var sources = manifest.EnergySources.Cast<object>().Prepend(new
        {
            type = "solar", stat_energy_from = "sensor.unrelated_solar_energy", name = "Unrelated Solar",
        });
        var devices = new object[]
        {
            new { stat_consumption = "sensor.unrelated_device_energy", stat_rate = "sensor.unrelated_device_power", name = "Unrelated Device" },
            new
            {
                stat_consumption = "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_23xload_x5fenergy_x5ftotal",
                stat_rate = "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_13xload_x5fpower",
                name = "HVO AC Load",
            },
            new
            {
                stat_consumption = "sensor.workshop_power_strip_plug_1_workshop_mr_cool_ac_today_s_consumption",
                included_in_stat = "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_23xload_x5fenergy_x5ftotal",
            },
        };
        return JsonSerializer.SerializeToElement(new
        {
            energy_sources = sources,
            device_consumption = devices,
            device_consumption_water = new[] { new { stat_consumption = "sensor.unrelated_water", name = "Unrelated Water" } },
        });
    }

    private static JsonElement ManagedPreferences(EnergyPreferencesManifest manifest) => JsonSerializer.SerializeToElement(new
    {
        energy_sources = manifest.EnergySources,
        device_consumption = manifest.DeviceConsumption,
        device_consumption_water = Array.Empty<object>(),
    });

    private static JsonElement EmptyPreferences() => JsonSerializer.SerializeToElement(new
    {
        energy_sources = Array.Empty<object>(),
        device_consumption = Array.Empty<object>(),
        device_consumption_water = Array.Empty<object>(),
    });

    private static JsonElement ValidEnergyValidation() => JsonSerializer.SerializeToElement(new
    {
        energy_sources = new[] { Array.Empty<object>() },
        device_consumption = new[] { Array.Empty<object>() },
        device_consumption_water = Array.Empty<object>(),
    });

    private static JsonElement InvalidEnergyValidation() => JsonSerializer.SerializeToElement(new
    {
        energy_sources = new[] { Array.Empty<object>() },
        device_consumption = new object[] { new[] { "entity_unavailable" } },
        device_consumption_water = Array.Empty<object>(),
    });

    private static JsonElement[] Entries(JsonElement preferences, string property) =>
        preferences.GetProperty(property).EnumerateArray().Select(static entry => entry.Clone()).ToArray();

    private static string?[] Names(JsonElement preferences, string property) => Entries(preferences, property)
        .Where(entry => entry.TryGetProperty("name", out _))
        .Select(entry => entry.GetProperty("name").GetString())
        .ToArray();

    private static string Json(JsonElement value) => JsonSerializer.Serialize(value);

    private static JsonElement State(string entityId, string state, string deviceClass, string stateClass, string unit) =>
        JsonSerializer.SerializeToElement(new
        {
            entity_id = entityId,
            state,
            attributes = new { device_class = deviceClass, state_class = stateClass, unit_of_measurement = unit },
        });

    private sealed class FakeClient : IHomeAssistantRegistryClient
    {
        private FakeClient(EnergyPreferencesManifest manifest, JsonElement preferences)
        {
            StoredPreferences = preferences.Clone();
            foreach (var property in manifest.EnergySources.Concat(manifest.DeviceConsumption)
                         .SelectMany(static entry => entry.EnumerateObject())
                         .Where(static property => property.Name.StartsWith("stat_", StringComparison.Ordinal)))
            {
                var entityId = property.Value.GetString()!;
                var rate = property.Name == "stat_rate";
                var soc = property.Name == "stat_soc";
                States[entityId] = State(entityId, soc ? "50" : "1", rate ? "power" : soc ? "battery" : "energy",
                    rate || soc ? "measurement" : "total_increasing", rate ? "W" : soc ? "%" : "kWh");
                Statistics[entityId] = JsonSerializer.SerializeToElement(new
                {
                    statistic_id = entityId,
                    has_mean = rate || soc,
                    has_sum = !rate && !soc,
                    unit_class = rate ? "power" : soc ? "unitless" : "energy",
                });
            }
        }

        public string Version => "2026.8.1";
        public JsonElement? StoredPreferences { get; private set; }
        public Dictionary<string, JsonElement> States { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Statistics { get; } = new(StringComparer.Ordinal);
        public Queue<JsonElement?> PreferenceReads { get; } = new();
        public Queue<Exception?> SaveOutcomes { get; } = new();
        public Queue<JsonElement> EnergyValidations { get; } = new();
        public List<JsonElement> SaveRequests { get; } = [];
        public List<CancellationToken> SaveTokens { get; } = [];
        public List<CancellationToken> PreferenceReadTokens { get; } = [];
        public CancellationTokenSource? CancelAfterFirstMutatingSave { get; set; }
        public int PreferenceReadCount { get; private set; }

        public static FakeClient For(EnergyPreferencesManifest manifest, JsonElement preferences) => new(manifest, preferences);
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<JsonElement[]> ListStatesAsync(CancellationToken cancellationToken) => Task.FromResult(States.Values.ToArray());
        public Task<JsonElement[]> ListStatisticIdsAsync(CancellationToken cancellationToken) => Task.FromResult(Statistics.Values.ToArray());
        public Task<JsonElement> GetConfigAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> GetDailyStatisticAsync(string statisticId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> GetStatisticsDuringPeriodAsync(
            IReadOnlyList<string> statisticIds,
            DateTimeOffset start,
            DateTimeOffset end,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement?> GetEnergyPreferencesAsync(CancellationToken cancellationToken)
        {
            PreferenceReadCount++;
            PreferenceReadTokens.Add(cancellationToken);
            return Task.FromResult(PreferenceReads.Count > 0 ? PreferenceReads.Dequeue() : StoredPreferences);
        }

        public Task<JsonElement> SaveEnergyPreferencesAsync(
            IReadOnlyList<JsonElement> energySources,
            IReadOnlyList<JsonElement> deviceConsumption,
            IReadOnlyList<JsonElement> waterConsumption,
            CancellationToken cancellationToken)
        {
            var request = JsonSerializer.SerializeToElement(new
            {
                energy_sources = energySources,
                device_consumption = deviceConsumption,
                device_consumption_water = waterConsumption,
            });
            SaveRequests.Add(request);
            SaveTokens.Add(cancellationToken);
            if (SaveRequests.Count == 1 && CancelAfterFirstMutatingSave is { } operationCancellation)
            {
                StoredPreferences = request;
                operationCancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            if (SaveOutcomes.Count > 0 && SaveOutcomes.Dequeue() is { } exception)
                throw exception;
            StoredPreferences = request;
            return Task.FromResult(request);
        }

        public Task<JsonElement> ValidateEnergyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EnergyValidations.Count > 0 ? EnergyValidations.Dequeue() : ValidEnergyValidation());
        public Task<JsonElement[]> ListEntitiesAsync(CancellationToken cancellationToken) => Task.FromResult(Array.Empty<JsonElement>());
        public Task<JsonElement> FindRelatedAsync(string entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CreateBackupAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JsonElement>> GetLovelaceConfigurationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JsonElement> RenameAsync(string sourceEntityId, string targetEntityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
