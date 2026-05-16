using Api.Common.Context;

namespace Api.Infrastructure.Security;

/// <summary>
/// Endpoint filter that enforces a single permission claim.
///
/// PRD v2.1 §4.1 — authorization is permission-string based, not role based.
/// Anonymous requests yield 401, authenticated-but-missing yield 403.
/// </summary>
internal sealed class RequirePermissionFilter(string permission) : IEndpointFilter
{
    private readonly string _permission = !string.IsNullOrWhiteSpace(permission)
        ? permission
        : throw new ArgumentException("Permission must be non-empty.", nameof(permission));

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (http.User?.Identity?.IsAuthenticated != true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                type: "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1",
                instance: http.Request.Path);
        }

        var requestContext = http.RequestServices.GetRequiredService<IRequestContext>();
        if (!requestContext.Permissions.Contains(_permission))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: $"Missing required permission '{_permission}'.",
                type: "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3",
                instance: http.Request.Path);
        }

        return await next(context);
    }
}

/// <summary>Convenience extension: <c>app.MapPost(...).RequirePermission("products.create")</c>.</summary>
public static class PermissionRouteHandlerExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter(new RequirePermissionFilter(permission));
        builder.RequireAuthorization();
        return builder;
    }
}
