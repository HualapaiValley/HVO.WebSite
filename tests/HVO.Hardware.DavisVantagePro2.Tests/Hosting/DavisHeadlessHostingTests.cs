using System.Net;
using System.Text.Json;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Cwop;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HVO.Hardware.DavisVantagePro2.Tests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class DavisHeadlessHostingTests
{
    private const string DiagnosticsKey = "davis-diagnostics-test-key";
    private const string CentralKey = "davis-central-test-key";
    private const string CwopPasscode = "24680";

    [TestMethod]
    public async Task DavisHost_ExposesProtectedHeadlessDiagnosticsAndRedactsConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"davis-host-{Guid.NewGuid():N}");
        var configDirectory = Path.Combine(root, "config");
        var dataDirectory = Path.Combine(root, "data");
        var secretsDirectory = Path.Combine(root, "secrets");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(secretsDirectory);
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "diagnostics-api-key"), DiagnosticsKey);
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "central-ingest-api-key"), CentralKey);
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "cwop-passcode"), CwopPasscode);

        try
        {
            using var environment = new EnvironmentVariableScope(Configuration(configDirectory, dataDirectory, secretsDirectory));
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDavisStation>();
                    services.AddSingleton<IDavisStation>(new FakeDavisStation { BlockConnects = true });
                });
            });
            using var client = factory.CreateClient();
            var cwopState = factory.Services.GetRequiredService<CwopPublisherState>();
            var observedAt = new DateTime(2026, 8, 14, 11, 59, 0, DateTimeKind.Utc);
            var attemptedAt = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
            var succeededAt = new DateTime(2026, 8, 14, 11, 55, 0, DateTimeKind.Utc);
            cwopState.Observed(observedAt);
            cwopState.Attempted(attemptedAt);
            cwopState.Succeeded(succeededAt);
            cwopState.Failed("transport");

            (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
            foreach (var path in new[] { "/diagnostics/health", "/diagnostics/status", "/diagnostics/outbox" })
                (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
            (await client.GetAsync("/diagnostics/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", DiagnosticsKey);

            using var response = await client.GetAsync("/diagnostics/status");
            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Should().Contain("hvo-davis-test");
            body.Should().NotContain(DiagnosticsKey);
            body.Should().NotContain(CentralKey);
            body.Should().NotContain(CwopPasscode);
            body.Should().NotContain("cwop-passcode");
            body.Should().NotContain("cwop.example.invalid");
            body.Should().NotContain("diagnostics-api-key");
            body.Should().NotContain("central-ingest-api-key");
            body.Should().NotContain(root);
            body.Should().Contain("externalDeliveries");
            body.Should().Contain("weather-underground");
            body.Should().Contain("cwop");
            using var document = JsonDocument.Parse(body);
            var cwop = document.RootElement.GetProperty("externalDeliveries")
                .EnumerateArray()
                .Single(delivery => delivery.GetProperty("name").GetString() == "cwop");
            cwop.GetProperty("enabled").GetBoolean().Should().BeTrue();
            cwop.GetProperty("lastObservationAtUtc").GetDateTime().Should().Be(observedAt);
            cwop.GetProperty("lastAttemptAtUtc").GetDateTime().Should().Be(attemptedAt);
            cwop.GetProperty("lastSuccessAtUtc").GetDateTime().Should().Be(succeededAt);
            cwop.GetProperty("consecutiveFailures").GetInt32().Should().Be(1);
            cwop.GetProperty("lastError").GetString().Should().Be("transport");
            var references = typeof(Program).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);
            references.Should().NotContain(name => name == "MudBlazor" || name == "HVO.WebSite.Themes");
            typeof(Program).Assembly.GetManifestResourceNames().Should().NotContain(name => name.Contains("Razor", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Dictionary<string, string?> Configuration(string config, string data, string secrets) => new()
    {
        ["HVO_EDGE_CONFIG_FILE"] = Path.Combine(config, "gateway.json"),
        ["Edge__Paths__ConfigurationFile"] = Path.Combine(config, "gateway.json"),
        ["Edge__Paths__ConfigDirectory"] = config,
        ["Edge__Paths__DataDirectory"] = data,
        ["Edge__Paths__SecretsDirectory"] = secrets,
        ["Edge__Runtime__ServiceName"] = "hvo-davis-test",
        ["Edge__Runtime__GatewayId"] = "davis",
        ["Edge__Runtime__GatewayType"] = "davis-vantage-pro2",
        ["Edge__Runtime__Domain"] = "Weather",
        ["Edge__Runtime__SourceId"] = "station-1",
        ["Edge__Runtime__SiteId"] = "test-site",
        ["Edge__Runtime__DeviceId"] = "station-1",
        ["Edge__Runtime__DisplayName"] = "Davis Test Collector",
        ["Edge__Runtime__DiagnosticsApiKeySecret"] = "diagnostics-api-key",
        ["Station__Host"] = "127.0.0.1",
        ["Station__StationId"] = "station-1",
        ["Station__ArchiveCatchupMode"] = "Enabled",
        ["Station__CentralIngestBaseEndpoint"] = "https://example.test/",
        ["Station__CentralApiKeySecret"] = "central-ingest-api-key",
        ["Station__LocalDatabasePath"] = Path.Combine(data, "davis-local.db"),
        ["Cwop__Enabled"] = "true",
        ["Cwop__StationId"] = "DW4515",
        ["Cwop__Host"] = "cwop.example.invalid",
        ["Cwop__Passcode"] = "",
        ["Cwop__PasscodeSecret"] = "cwop-passcode",
        ["HomeAssistant__Mqtt__Enabled"] = "false",
        ["Outbox__DatabasePath"] = Path.Combine(data, "outbox.db"),
        ["Outbox__PayloadTypes__0"] = "com.hvo.weather.raw.v1",
        ["Outbox__PayloadTypes__1"] = "com.hvo.weather.archive.v1",
        ["Outbox__PayloadVersion"] = "1",
        ["Outbox__SentRetentionDays"] = "0",
        ["Outbox__FailedRetentionDays"] = "0",
        ["ASPNETCORE_ENVIRONMENT"] = "Testing",
    };

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previous = [];
        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                previous[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
        public void Dispose()
        {
            foreach (var pair in previous)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
