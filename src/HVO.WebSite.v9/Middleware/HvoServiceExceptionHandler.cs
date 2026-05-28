using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HVO.WebSite.v9.Middleware;

/// <summary>
/// Global exception handler that converts exceptions to ProblemDetails responses
/// </summary>
/// <param name="problemDetailsService">Service for creating ProblemDetails responses</param>
/// <param name="logger">Logger for exception handling operations</param>
public class HvoServiceExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<HvoServiceExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Attempts to handle an exception by converting it to a ProblemDetails response
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request</param>
    /// <param name="exception">The exception that was thrown</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>True if the exception was handled, false otherwise</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var isDevelopment = httpContext.RequestServices
            .GetRequiredService<IHostEnvironment>()
            .IsDevelopment();

        var status = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status400BadRequest
                ? "The request is invalid."
                : "An unexpected error occurred.",
            Type = isDevelopment ? exception.GetType().Name : null,
            Detail = isDevelopment
                ? exception.Message
                : "The server could not complete the request. Use the traceId when contacting support."
        };

        logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            Exception = exception,
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });
    }
}
