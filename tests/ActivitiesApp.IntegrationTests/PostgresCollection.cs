using Xunit;

namespace ActivitiesApp.IntegrationTests;

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
