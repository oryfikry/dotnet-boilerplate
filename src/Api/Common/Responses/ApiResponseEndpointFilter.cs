using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Common.Responses;

/// <summary>
/// Endpoint filter that transparently wraps successful responses in
/// <see cref="ApiResponse{T}"/>.
///
/// Per PRD v2.1 §6 directive #6, endpoint handlers return <c>TypedResults.*</c>
/// without manual wrapping. This filter inspects the produced result and, when
/// it carries a payload that is not yet an <see cref="ApiResponse{T}"/>, replaces
/// it with an <c>Ok(ApiResponse&lt;T&gt;)</c>.
///
/// Error paths (ProblemDetails) and empty results (NoContent, Created without
/// body) are passed through untouched.
/// </summary>
internal sealed class ApiResponseEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var result = await next(context);
        var traceId = context.HttpContext.TraceIdentifier;

        // Unwrap Results<TR1, TR2, ...> discriminated unions to their active
        // inner result (Ok<T>, NotFound, etc.).
        result = UnwrapResultsUnion(result);

        // Already-wrapped (returned from handler explicitly).
        if (result is ApiResponse) return result;
        if (result is IValueHttpResult { Value: { } existing } && IsAlreadyWrapped(existing))
        {
            return result;
        }

        // Wrap Ok<T> / Created<T> / similar single-payload results.
        if (result is IValueHttpResult { Value: { } value })
        {
            return Results.Ok(WrapInResponse(value, traceId));
        }

        return result;
    }

    private static object? UnwrapResultsUnion(object? result)
    {
        if (result is null) return null;

        var type = result.GetType();
        if (!type.IsGenericType) return result;

        var def = type.GetGenericTypeDefinition();
        if (def.FullName?.StartsWith("Microsoft.AspNetCore.Http.HttpResults.Results`", StringComparison.Ordinal) != true)
        {
            return result;
        }

        // Results<T1, T2, ...> exposes a `Result` property of type IResult.
        var prop = type.GetProperty("Result");
        return prop?.GetValue(result) ?? result;
    }

    private static bool IsAlreadyWrapped(object value) =>
        value is ApiResponse
        || (value.GetType().IsGenericType
            && value.GetType().GetGenericTypeDefinition() == typeof(ApiResponse<>));

    private static object WrapInResponse(object value, string traceId)
    {
        // Reflectively call ApiResponse<T>.Ok(value, traceId) so the wrapper
        // preserves the runtime type for OpenAPI/serialization.
        var responseType = typeof(ApiResponse<>).MakeGenericType(value.GetType());
        var ok = responseType.GetMethod(
            nameof(ApiResponse<object>.Ok),
            [value.GetType(), typeof(string)])!;

        return ok.Invoke(null, [value, traceId])!;
    }
}
