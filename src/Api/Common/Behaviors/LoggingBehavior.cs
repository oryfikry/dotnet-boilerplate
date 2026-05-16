using System.Diagnostics;
using MediatR;

namespace Api.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that emits structured entry/exit logs and a
/// duration measurement for every request.
///
/// Per PRD v2.1 §8 (DoD), each handler exposes structured logging — this
/// behavior provides the baseline so individual handlers don't have to.
/// </summary>
internal sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        LogStart(logger, requestName);

        try
        {
            var response = await next();
            sw.Stop();
            LogSuccess(logger, requestName, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogFailure(logger, ex, requestName, sw.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Handling {RequestName}")]
    private static partial void LogStart(ILogger logger, string requestName);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMs} ms")]
    private static partial void LogSuccess(ILogger logger, string requestName, long elapsedMs);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Failed {RequestName} in {ElapsedMs} ms with {ExceptionType}")]
    private static partial void LogFailure(
        ILogger logger, Exception ex, string requestName, long elapsedMs, string exceptionType);
}
