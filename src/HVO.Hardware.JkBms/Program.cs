using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Hardware.JkBms.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddHvoEdgeRuntime();
builder.Services.AddHvoHomeAssistantMqtt(builder.Configuration);
builder.Services.AddJkBmsCollector(builder.Configuration);
builder.Services.AddHvoHomeAssistantGatewayDiagnostics();

var app = builder.Build();
// The deployed volume may contain the pre-vNext JK schema. This must run before
// EdgeOutboxInitializer validates the shared schema during host startup.
await app.Services.MigrateJkBmsLegacyOutboxAsync();
app.MapHvoEdgeRuntimeEndpoints();
app.Run();

public partial class Program;
