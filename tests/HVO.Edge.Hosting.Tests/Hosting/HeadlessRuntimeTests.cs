using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting;
using HVO.Edge.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.Edge.Hosting.Tests.Hosting;

[TestClass]
public sealed class HeadlessRuntimeTests
{
    [TestMethod]
    public async Task MissingDeviceHealthProvider_IsNotReadyByDefault()
    {
        var snapshot = await new HVO.Edge.Hosting.Diagnostics.DefaultEdgeDiagnosticsSnapshotProvider()
            .GetSnapshotAsync(CancellationToken.None);

        snapshot.Health.State.Should().Be(GatewayHealthState.Critical);
        snapshot.Health.Alerts.Should().ContainSingle(alert => alert.Code == "device-health-provider-missing");
    }

    [TestMethod]
    public async Task ReferenceHost_ExposesOnlyHeadlessOperationalEndpoints()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        var references = typeof(Program).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);
        references.Should().NotContain(name => name == "MudBlazor" || name == "HVO.WebSite.Themes");
        typeof(Program).Assembly.GetManifestResourceNames().Should().NotContain(name => name.Contains("Razor", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Diagnostics_RequireSecretFileCredentialAndExcludeSensitivePaths()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/diagnostics/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        (await client.GetAsync("/diagnostics/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-diagnostics-key");

        var response = await client.GetAsync("/diagnostics/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("hvo-edge-test");
        body.Should().NotContain("test-diagnostics-key");
        body.Should().NotContain("diagnostics-api-key");
        body.Should().NotContain("outbox.db");
        body.Should().NotContain("gateway.json");
    }

    [TestMethod]
    public async Task OutboxSettings_RejectEmptyRequestBody()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-diagnostics-key");

        var response = await client.PutAsync("/diagnostics/outbox/settings", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Request body is required.");
    }

    [TestMethod]
    public async Task OtlpOutage_DoesNotStopAcquisitionOrOutbox()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = "http://127.0.0.1:1",
            ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "http://127.0.0.1:1",
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = "http://127.0.0.1:1",
            ["HVO_LOGGING_ENABLE_OTLP_IN_TESTS"] = "true"
        });
        using var client = factory.CreateClient();

        await using (var enqueueScope = factory.Services.CreateAsyncScope())
        {
            var store = enqueueScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
            await store.EnqueueAsync(new EdgeOutboxMessage(
                "test-source",
                DateTime.UtcNow,
                "test.observation",
                "1",
                "{\"value\":42}"), CancellationToken.None);
        }

        var runtime = factory.Services.GetRequiredService<TestAcquisitionState>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((runtime.Ticks == 0 || runtime.SentRecords == 0) && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        runtime.Ticks.Should().BeGreaterThan(0);
        runtime.SentRecords.Should().Be(1);
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-diagnostics-key");
        var status = await client.GetFromJsonAsync<GatewayDiagnosticStatusResponse>("/diagnostics/status");
        status!.Telemetry.OtlpEndpointConfigured.Should().BeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>();
        (await EdgeOutboxDiagnosticsReader.ReadAsync(db)).Schema.IsCompatible.Should().BeTrue();
    }

    [TestMethod]
    public async Task RuntimeIdentity_IsSharedByDiagnosticsAndTelemetryResources()
    {
        await using var factory = CreateFactory();
        var identity = factory.Services.GetRequiredService<EdgeRuntimeIdentity>();
        var resources = identity.CreateResourceAttributes().ToDictionary(pair => pair.Key, pair => pair.Value);

        resources[GatewayTelemetryConventions.ResourceAttributes.ServiceName].Should().Be(identity.ServiceName);
        resources[GatewayTelemetryConventions.ResourceAttributes.ServiceVersion].Should().Be(identity.ServiceVersion);
        resources[GatewayTelemetryConventions.ResourceAttributes.ServiceInstanceId].Should().Be(identity.ServiceInstanceId);
        resources[GatewayTelemetryConventions.ResourceAttributes.GatewayId].Should().Be(identity.GatewayId);
        resources[GatewayTelemetryConventions.ResourceAttributes.GatewayType].Should().Be(identity.GatewayType);
        resources[GatewayTelemetryConventions.ResourceAttributes.SiteId].Should().Be(identity.SiteId);
        identity.DisplayName.Should().Be("Mounted Headless Test Host");
    }

    private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? configuration = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            if (configuration is not null)
                builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(configuration));
        });
}
