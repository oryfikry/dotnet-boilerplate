using Xunit;

namespace Api.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection definition that shares a single
/// <see cref="IntegrationTestWebAppFactory"/> (and therefore a single
/// Postgres Testcontainer) across every test class in the suite.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestWebAppFactory>
{
    public const string Name = "Integration";
}
