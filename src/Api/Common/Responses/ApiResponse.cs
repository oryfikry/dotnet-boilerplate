namespace Api.Common.Responses;

/// <summary>
/// Success-only response wrapper.
///
/// Per PRD v2.1 §4.4, error paths always emit RFC 7807 ProblemDetails;
/// successful 2xx responses are wrapped in <see cref="ApiResponse{T}"/>.
/// Endpoints return <c>TypedResults.Ok(value)</c> directly — wrapping happens
/// transparently in <see cref="ApiResponseEndpointFilter"/>.
/// </summary>
public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Message = null,
    string? TraceId = null)
{
    public static ApiResponse<T> Ok(T data, string? traceId = null) =>
        new(true, data, null, traceId);

    public static ApiResponse<T> Ok(T data, string message, string? traceId = null) =>
        new(true, data, message, traceId);
}

/// <summary>
/// Untyped marker for 204 No Content style responses.
/// </summary>
public sealed record ApiResponse(
    bool Success,
    string? Message = null,
    string? TraceId = null)
{
    public static ApiResponse Ok(string? message = null, string? traceId = null) =>
        new(true, message, traceId);
}
