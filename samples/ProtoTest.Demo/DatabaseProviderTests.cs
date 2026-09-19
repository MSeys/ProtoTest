namespace ProtoTest.Demo;

/// <summary>
/// Configuration decides the store provider before any infrastructure starts; an external PostgreSQL
/// connection string must never be handed to the SQLite provider.
/// </summary>
[TestFixture]
public sealed class DatabaseProviderTests
{
    [TestCase(null, null, false)]
    [TestCase("sqlite", "Data Source=northstar.db", false)]
    [TestCase("sqlite", "Host=db;Database=northstar;Username=app;Password=secret", false)]
    [TestCase("postgres", "Data Source=northstar.db", true)]
    [TestCase("postgresql", null, true)]
    [TestCase(null, "Host=db;Database=northstar;Username=app;Password=secret", true)]
    [TestCase(null, "postgres://app:secret@db:5432/northstar", true)]
    [TestCase(null, "postgresql://app:secret@db:5432/northstar", true)]
    [TestCase(null, "Data Source=file:northstar;Mode=Memory;Cache=Shared;Pooling=False", false)]
    public void IsPostgresStore_ShouldDeriveTheProviderFromConfiguration(
        string? provider,
        string? connectionString,
        bool expected)
        => Assert.That(Setup.IsPostgresStore(provider, connectionString), Is.EqualTo(expected));
}
