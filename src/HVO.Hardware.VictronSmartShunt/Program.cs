using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Hardware.VictronSmartShunt.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddHvoEdgeRuntime();
builder.Services.AddHvoHomeAssistantMqtt(builder.Configuration);
builder.Services.AddSmartShuntCollector(builder.Configuration);
builder.Services.AddHvoHomeAssistantGatewayDiagnostics();

var app = builder.Build();
await app.Services.MigrateSmartShuntLegacyOutboxAsync();
app.MapHvoEdgeRuntimeEndpoints();
app.Run();

public partial class Program;
