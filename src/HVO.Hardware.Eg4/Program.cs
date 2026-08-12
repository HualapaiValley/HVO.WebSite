using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Hardware.Eg4.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddHvoEdgeRuntime();
builder.Services.AddHvoHomeAssistantMqtt(builder.Configuration);
builder.Services.AddEg4Collector(builder.Configuration, builder.Environment);

var app = builder.Build();
app.MapHvoEdgeRuntimeEndpoints();
app.Run();

public partial class Program;
