using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HVO.WebSite.Playground.Middleware;

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
            var problemDetails = new ProblemDetails
            {
                Status = exception switch
                {
                    ArgumentException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError
                },
                Title = "An error occurred",
                Type = exception.GetType().Name,
                Detail = exception.Message
            };

            logger.LogError(exception, "Problem Details: {@problemDetails}", problemDetails);

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                Exception = exception,
                HttpContext = httpContext,
                ProblemDetails = problemDetails
            });
        }
    }
