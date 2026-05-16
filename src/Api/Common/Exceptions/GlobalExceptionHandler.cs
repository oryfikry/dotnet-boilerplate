using FluentValidation;
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
/// FluentValidation <see cref="ValidationException"/> is treated specially:
/// it produces an <see cref="HttpValidationProblemDetails"/> with per-field
/// errors, matching the shape that <c>ProducesValidationProblem()</c> declares
/// in OpenAPI documentation.
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

        ProblemDetails problem = exception switch
        {
            ValidationException ve => BuildValidationProblem(ve, httpContext),
            _ => BuildGenericProblem(exception, httpContext)
        };

        // Log at the level appropriate to the status class.
        if ((problem.Status ?? StatusCodes.Status500InternalServerError) >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(logger, exception, exception.Message);
        }
        else
        {
            LogHandledDomainException(logger, exception, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }

    private static HttpValidationProblemDetails BuildValidationProblem(
        ValidationException ve, HttpContext httpContext)
    {
        var errors = ve.Errors
            .GroupBy(f => f.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).ToArray());

        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        return problem;
    }

    private ProblemDetails BuildGenericProblem(Exception exception, HttpContext httpContext)
    {
        var (status, title, type) = MapException(exception);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = env.IsDevelopment() ? exception.ToString() : exception.Message,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        return problem;
    }

    private static (int Status, string Title, string Type) MapException(Exception ex) =>
        ex switch
        {
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
