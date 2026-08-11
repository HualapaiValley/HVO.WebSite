using HVO.Edge.Exporter.HomeAssistant;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.AddHvoEdgeRuntime();
builder.Services.AddHomeAssistantExporter(builder.Configuration);

var app = builder.Build();
app.MapHvoEdgeRuntimeEndpoints();
app.Run();

public partial class Program;
