using System.Text.Json;
using FluentAssertions;
using HVO.WebSite.v9.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HvoServiceExceptionHandlerTests
{
    [TestMethod]
    public async Task TryHandleAsync_ArgumentException_ReturnsBadRequest()
    {
        var (handled, context, problem) = await HandleAsync(new ArgumentException("bad argument"), Environments.Production);

        handled.Should().BeTrue();
        problem.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        problem.GetProperty("title").GetString().Should().Be("The request is invalid.");
    }

    [TestMethod]
    public async Task TryHandleAsync_GenericException_ReturnsInternalServerError()
    {
        var (handled, context, problem) = await HandleAsync(new InvalidOperationException("boom"), Environments.Production);

        handled.Should().BeTrue();
        problem.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
        problem.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
    }

    [TestMethod]
    public async Task TryHandleAsync_Production_DoesNotExposeExceptionDetails()
    {
        var (_, _, problem) = await HandleAsync(new InvalidOperationException("secret failure detail"), Environments.Production);

        problem.GetProperty("detail").GetString().Should().Be("The server could not complete the request. Use the traceId when contacting support.");
        problem.GetProperty("type").GetString().Should().NotBe(nameof(InvalidOperationException));
        problem.ToString().Should().NotContain("secret failure detail");
    }

    [TestMethod]
    public async Task TryHandleAsync_Development_IncludesExceptionDetails()
    {
        var (_, _, problem) = await HandleAsync(new InvalidOperationException("developer failure detail"), Environments.Development);

        problem.GetProperty("detail").GetString().Should().Be("developer failure detail");
        problem.GetProperty("type").GetString().Should().Be(nameof(InvalidOperationException));
    }

    private static async Task<(bool Handled, DefaultHttpContext Context, JsonElement Problem)> HandleAsync(Exception exception, string environmentName)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddProblemDetails();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            Response = { Body = new MemoryStream() },
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/test";

        var handler = new HvoServiceExceptionHandler(
            provider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<HvoServiceExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        return (handled, context, problem);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HVO.WebSite.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
