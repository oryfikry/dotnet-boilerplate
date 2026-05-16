using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Api.Common.Exceptions;

/// <summary>
/// Global <see cref="IExceptionHandler"/> emitting RFC 7807 ProblemDetails.
///
/// Per PRD v2.1 §4.4, every unhandled exception is converted into a
/// ProblemDetails payload with a stable <c>type</c> URI and a correlation
/// <c>traceId</c> taken from <see cref="HttpContext.TraceIdentifier"/>.
///
/// Concrete exception type → status code mapping is centralised here so
/// individual feature handlers never need to deal with HTTP semantics.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment env,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var (status, title, type) = MapException(exception);

        // Log at the level appropriate to the status class.
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(logger, exception, exception.Message);
        }
        else
        {
            LogHandledDomainException(logger, exception, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = env.IsDevelopment() ? exception.ToString() : exception.Message,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }

    private static (int Status, string Title, string Type) MapException(Exception ex) =>
        ex switch
        {
            // Add more domain exceptions here as the codebase grows.
            ArgumentException        => (StatusCodes.Status400BadRequest,
                                         "Bad Request",
                                         "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized,
                                            "Unauthorized",
                                            "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1"),
            KeyNotFoundException     => (StatusCodes.Status404NotFound,
                                         "Not Found",
                                         "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4"),
            NotImplementedException  => (StatusCodes.Status501NotImplemented,
                                         "Not Implemented",
                                         "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.2"),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest,
                                           "Client Closed Request",
                                           "https://httpstatuses.com/499"),
            _                        => (StatusCodes.Status500InternalServerError,
                                         "Internal Server Error",
                                         "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1")
        };

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled exception: {Message}")]
    private static partial void LogUnhandledException(ILogger logger, Exception ex, string message);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Handled domain exception: {Message}")]
    private static partial void LogHandledDomainException(ILogger logger, Exception ex, string message);
}
