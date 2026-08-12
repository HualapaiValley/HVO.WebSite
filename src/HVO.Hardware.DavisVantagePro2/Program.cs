using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Hardware.DavisVantagePro2.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddHvoEdgeRuntime();
builder.Services.AddHvoHomeAssistantMqtt(builder.Configuration);
builder.Services.AddDavisCollector(builder.Configuration);

var app = builder.Build();
// The deployed davis-outbox volume can contain both the pre-shared schema and
// legacy payload identifiers. Upgrade it before the shared initializer validates it.
await app.Services.MigrateDavisLocalStateAsync();
app.MapHvoEdgeRuntimeEndpoints();
app.Run();

public partial class Program;
