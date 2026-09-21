namespace ProtoTest.Demo;

using Microsoft.Extensions.Configuration;

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
        => Assert.That(DemoEnvironment.IsPostgresStore(provider, connectionString), Is.EqualTo(expected));

    [Test]
    public void Environment_ShouldDescribeTheDefaultLocalRun()
    {
        var environment = CreateEnvironment();

        Assert.Multiple(() =>
        {
            Assert.That(environment.UsesLocalApplications, Is.True);
            Assert.That(environment.RunsStandaloneConsole, Is.True);
            Assert.That(environment.UsesPostgres, Is.False);
            Assert.That(environment.CanComposeDomain, Is.True);
            Assert.That(environment.UsesMessaging, Is.False);
        });
    }

    [Test]
    public void Environment_ShouldDescribeAPublishedRunWithoutStoreAccess()
    {
        var environment = CreateEnvironment(("ProtoTest:TargetUrl", "https://northstar.example"));

        Assert.Multiple(() =>
        {
            Assert.That(environment.UsesLocalApplications, Is.False);
            Assert.That(environment.RunsStandaloneConsole, Is.False);
            Assert.That(environment.CanComposeDomain, Is.False);
        });
    }

    [Test]
    public void Environment_ShouldDescribeRunOwnedContainers()
    {
        var environment = CreateEnvironment(
            ("ProtoTest:Database", "postgres"),
            ("ProtoTest:Messaging:Broker", "container"));

        Assert.Multiple(() =>
        {
            Assert.That(environment.OwnsPostgres, Is.True);
            Assert.That(environment.UsesPostgres, Is.True);
            Assert.That(environment.RunsStandaloneConsole, Is.False);
            Assert.That(environment.OwnsMessagingBroker, Is.True);
            Assert.That(environment.UsesMessaging, Is.True);
        });
    }

    [Test]
    public void Environment_ShouldRecognizeExternalInfrastructure()
    {
        var environment = CreateEnvironment(
            ("ProtoTest:TargetUrl", "https://northstar.example"),
            ("ConnectionStrings:Northstar", "Host=db;Database=northstar;Username=app;Password=secret"),
            ("ProtoTest:Messaging:RabbitMq:ConnectionString", "amqp://guest:guest@broker"));

        Assert.Multiple(() =>
        {
            Assert.That(environment.OwnsPostgres, Is.False);
            Assert.That(environment.UsesPostgres, Is.True);
            Assert.That(environment.CanComposeDomain, Is.True);
            Assert.That(environment.OwnsMessagingBroker, Is.False);
            Assert.That(environment.UsesMessaging, Is.True);
        });
    }

    private static DemoEnvironment CreateEnvironment(params (string Key, string? Value)[] settings)
        => DemoEnvironment.From(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => setting.Value))
            .Build());
}
