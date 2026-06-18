using System.Reflection;
using Bunit;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Components.Pages;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace HVO.Gateway.SolarAssistant.Tests.Components;

[TestClass]
public sealed class RestInventoryPageBunitTests : BunitContext
{
    public RestInventoryPageBunitTests() => Services.AddMudServices();

    [TestMethod]
    public void RendersRestMetricInventory()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var worker = new SolarAssistantSnapshotWorker(provider.GetRequiredService<IServiceScopeFactory>(), new EmptySolarAssistantClient(), Options.Create(new SolarAssistantOptions()), NullLogger<SolarAssistantSnapshotWorker>.Instance);
        typeof(SolarAssistantSnapshotWorker).GetField("_lastInventory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, new SolarAssistantMetricInventory
        {
            RecordedAtUtc = DateTime.UtcNow,
            MetricCount = 2,
            Topics = [new SolarAssistantMetricSummary { Topic = "total/pv_power", Group = "total", Name = "PV Power", Unit = "W", Classification = SolarAssistantMetricClassification.DbCandidate }],
            ClassificationCounts = new Dictionary<string, int> { [SolarAssistantMetricClassification.DbCandidate] = 1 },
            GroupCounts = new Dictionary<string, int> { ["total"] = 1 },
            UnitCounts = new Dictionary<string, int> { ["W"] = 1 }
        });
        Services.AddSingleton(worker);

        var component = Render<RestInventory>();

        component.Markup.Should().Contain("SolarAssistant REST Topics");
        component.Markup.Should().Contain("total/pv_power");
        component.Markup.Should().Contain("PV Power");
        component.Markup.Should().Contain("db_candidate");
        worker.Dispose();
    }

    private sealed class EmptySolarAssistantClient : ISolarAssistantClient
    {
        public Task<IReadOnlyList<SolarAssistantMetric>> GetMetricsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SolarAssistantMetric>>([]);
    }
}
