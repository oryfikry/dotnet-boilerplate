namespace Api.Common.Endpoints;

/// <summary>
/// Marker interface for VSA endpoint classes.
///
/// Per PRD v2.1 §6 directive #2 and §7.1, every Minimal API endpoint must:
/// 1. Implement <see cref="IEndpoint"/>.
/// 2. Provide a <c>public static void MapEndpoint(IEndpointRouteBuilder app)</c> method.
///
/// Endpoints are auto-discovered at startup via
/// <see cref="EndpointRegistrationExtensions.MapEndpoints"/>.
/// </summary>
public interface IEndpoint
{
    static abstract void MapEndpoint(IEndpointRouteBuilder app);
}
