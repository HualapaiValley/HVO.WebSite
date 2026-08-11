using FluentAssertions;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Hosting.Tests.Configuration;

[TestClass]
public sealed class EdgeConfigurationTests
{
    [TestMethod]
    public void PathValidation_RejectsRelativeAndEscapingPaths()
    {
        var validator = new EdgePathOptionsValidator();

        validator.Validate(null, new EdgePathOptions { DataDirectory = "relative" }).Failed.Should().BeTrue();
        validator.Validate(null, new EdgePathOptions
        {
            ConfigDirectory = "/app/config",
            ConfigurationFile = "/outside/gateway.json"
        }).Failed.Should().BeTrue();
    }

    [TestMethod]
    public void SecretResolver_RejectsTraversalAndPlaceholderContent()
    {
        var root = Directory.CreateTempSubdirectory("hvo-edge-secrets-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "placeholder"), "REPLACE_ME");
            var resolver = new SecretFileResolver(Options.Create(new EdgePathOptions
            {
                ConfigDirectory = root.FullName,
                ConfigurationFile = Path.Combine(root.FullName, "gateway.json"),
                DataDirectory = root.FullName,
                SecretsDirectory = root.FullName
            }));

            var traversal = () => resolver.ReadRequired("../outside", "Secret");
            traversal.Should().Throw<InvalidOperationException>().WithMessage("*file name relative to the secrets directory*");
            var placeholder = () => resolver.ReadRequired("placeholder", "Secret");
            placeholder.Should().Throw<InvalidOperationException>().WithMessage("*empty or a placeholder*");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SecretResolver_RejectsSymbolicLinks()
    {
        var root = Directory.CreateTempSubdirectory("hvo-edge-secret-link-");
        try
        {
            var secrets = Directory.CreateDirectory(Path.Combine(root.FullName, "secrets"));
            var outside = Path.Combine(root.FullName, "outside-secret");
            File.WriteAllText(outside, "outside-value");
            File.CreateSymbolicLink(Path.Combine(secrets.FullName, "linked-secret"), outside);
            var resolver = new SecretFileResolver(Options.Create(new EdgePathOptions
            {
                ConfigDirectory = root.FullName,
                ConfigurationFile = Path.Combine(root.FullName, "gateway.json"),
                DataDirectory = root.FullName,
                SecretsDirectory = secrets.FullName
            }));

            var read = () => resolver.ReadRequired("linked-secret", "Secret");

            read.Should().Throw<InvalidOperationException>().WithMessage("*must not be a symbolic link*");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void MountedConfiguration_RejectsSelectedFileOutsideConfigRoot()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [EdgeConfigurationBuilderExtensions.ConfigurationFileEnvironmentVariable] = "/outside/gateway.json",
            ["Edge:Paths:ConfigDirectory"] = "/app/config"
        });

        var add = () => builder.AddHvoEdgeMountedConfiguration();

        add.Should().Throw<InvalidOperationException>().WithMessage("*must select an absolute file under*");
    }

    [TestMethod]
    public async Task InvalidPersistentPath_FailsBeforeDeviceWorkerStarts()
    {
        var root = Directory.CreateTempSubdirectory("hvo-edge-startup-");
        try
        {
            var configDirectory = Path.Combine(root.FullName, "config");
            var dataDirectory = Path.Combine(root.FullName, "data");
            var secretsDirectory = Path.Combine(root.FullName, "secrets");
            Directory.CreateDirectory(configDirectory);
            Directory.CreateDirectory(secretsDirectory);
            File.WriteAllText(Path.Combine(secretsDirectory, "diagnostics"), "valid-test-key");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Edge:Runtime:ServiceName"] = "invalid-host",
                ["Edge:Runtime:GatewayId"] = "invalid-host",
                ["Edge:Runtime:GatewayType"] = "test",
                ["Edge:Runtime:DiagnosticsApiKeySecret"] = "diagnostics",
                ["Edge:Paths:ConfigurationFile"] = Path.Combine(configDirectory, "gateway.json"),
                ["Edge:Paths:ConfigDirectory"] = configDirectory,
                ["Edge:Paths:DataDirectory"] = dataDirectory,
                ["Edge:Paths:SecretsDirectory"] = secretsDirectory,
                ["Outbox:DatabasePath"] = Path.Combine(root.FullName, "outside", "outbox.db"),
                ["Outbox:PayloadType"] = "test",
                ["Outbox:PayloadVersion"] = "1"
            });
            builder.AddHvoEdgeRuntime();
            builder.Services.AddSingleton<IEdgeOutboxBatchSender, NoOpSender>();
            builder.Services.AddSingleton<WorkerState>();
            builder.Services.AddHostedService<ProbeWorker>();
            await using var app = builder.Build();

            var start = () => app.StartAsync();

            await start.Should().ThrowAsync<OptionsValidationException>()
                .WithMessage("*Outbox:DatabasePath must remain under*");
            app.Services.GetRequiredService<WorkerState>().Started.Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class WorkerState
    {
        public bool Started { get; set; }
    }

    private sealed class ProbeWorker(WorkerState state) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            state.Started = true;
            return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
    }

    private sealed class NoOpSender : IEdgeOutboxBatchSender
    {
        public Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
            IReadOnlyList<EdgeOutboxRecord> records,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EdgeOutboxSendOutcome>>([]);
    }
}
