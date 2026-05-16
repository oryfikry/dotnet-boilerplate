using System.Reflection;

namespace Api.Common.Endpoints;

/// <summary>
/// Reflection-based auto-discovery of <see cref="IEndpoint"/> implementations.
///
/// Per PRD v2.1 §7.1, scans the API assembly for non-abstract classes that
/// implement <see cref="IEndpoint"/> and invokes their static
/// <c>MapEndpoint(IEndpointRouteBuilder)</c> method.
///
/// AOT note: when the AOT profile (Milestone 6) is enabled, this reflection
/// path is replaced by a source-generated registration. See PRD §3.2.
/// </summary>
internal static class EndpointRegistrationExtensions
{
    private const string MapEndpointMethodName = nameof(IEndpoint.MapEndpoint);

    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var assembly = typeof(EndpointRegistrationExtensions).Assembly;

        var endpointTypes = assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IEndpoint).IsAssignableFrom(t));

        foreach (var type in endpointTypes)
        {
            var map = type.GetMethod(
                MapEndpointMethodName,
                BindingFlags.Public | BindingFlags.Static);

            if (map is null)
            {
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' implements IEndpoint but does not expose " +
                    $"'public static void {MapEndpointMethodName}(IEndpointRouteBuilder)'.");
            }

            map.Invoke(null, [app]);
        }

        return app;
    }
}
