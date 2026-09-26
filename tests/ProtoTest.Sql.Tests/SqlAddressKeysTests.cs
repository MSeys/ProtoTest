namespace ProtoTest.Sql.Tests;

using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Pins the SQL integration's address rule: with <see cref="SqlOptions.AddressKeys"/>
/// declared and none provided, the Store capability is absent, setup opens nothing, and the accessors
/// name the missing keys and the capability gate. Without declared keys the behavior is unchanged.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SqlAddressKeysTests
{
    private const string AddressKey = "ConnectionStrings:Csms";

    [Test]
    public async Task AddSql_WithDeclaredAddressKeys_WhenNoneIsProvided_ShouldBeInertAndNameTheKeys()
    {
        using var database = new SqliteKeeper($"sql-inert-{Guid.NewGuid():N}");
        DbConnection? created = null;
        var output = Path.Combine(Path.GetTempPath(), $"prototest-sql-inert-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddSql(
                _ => created = new SqliteConnection(database.ConnectionString),
                sql => sql.AddressKeys.Add(AddressKey));
            await using var host = builder.Build();
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.False);

            await host.StartAsync();
            await host.StartTestAsync("sql inert", TestMethods.Placeholder);

            ProtoSqlSession? session = null;
            var exception = Assert.Throws<InvalidOperationException>(() => session = Proto.Context.SqlSession());
            Assert.Throws<InvalidOperationException>(() => Proto.Context.SqlConnection());
            Assert.Throws<InvalidOperationException>(() => Proto.Context.SqlTransaction());
            var databaseResources = Proto.Context.Resources.Count(resource => resource.Kind == "database");

            var snapshot = host.Trace.Snapshot();
            var testKinds = snapshot.Tests.Single().Entries.Select(entry => entry.Kind).ToArray();
            var skipped = snapshot.Entries!.Single(entry => entry.Kind == "capability.skipped");
            await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
            await host.StopAsync();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.Null, "the connection factory is not called during an inert setup");
                Assert.That(databaseResources, Is.Zero, "nothing owns a connection");
                Assert.That(testKinds, Does.Not.Contain("sql.connection.open"));
                Assert.That(skipped.Attributes["capability.keys"], Does.Contain(AddressKey));
                Assert.That(skipped.Attributes["capability.reason"], Is.EqualTo("no key provided"));
                Assert.That(session, Is.Null);
                Assert.That(exception!.Message, Does.Contain(AddressKey));
                Assert.That(exception.Message, Does.Contain("RequiresCapability(ProtoCapabilityKinds.Store)"));
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task AddSql_WithDeclaredAddressKeys_WhenTheKeyIsConfigured_ShouldOpenTheConnection()
    {
        using var database = new SqliteKeeper($"sql-configured-{Guid.NewGuid():N}");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [AddressKey] = database.ConnectionString
            }));
        builder.AddSql(
            _ => new SqliteConnection(database.ConnectionString),
            sql => sql.AddressKeys.Add(AddressKey));
        await using var host = builder.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.True);

        await host.StartAsync();
        await host.StartTestAsync("sql configured", TestMethods.Placeholder);

        Assert.Multiple(() =>
        {
            Assert.That(Proto.Context.SqlConnection().State, Is.EqualTo(ConnectionState.Open));
            Assert.That(Proto.Context.SqlTransaction(), Is.Not.Null);
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddSql_WithDeclaredAddressKeys_WhenInfrastructureDeclaresTheKey_ShouldOpenAfterItStarts()
    {
        using var database = new SqliteKeeper($"sql-infrastructure-{Guid.NewGuid():N}");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(
            new DeclaredSettingsInfrastructure("database:csms", AddressKey, database.ConnectionString),
            AddressKey);
        builder.AddSql(
            _ => new SqliteConnection(database.ConnectionString),
            sql => sql.AddressKeys.Add(AddressKey));
        await using var host = builder.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.True);

        await host.StartAsync();
        await host.StartTestAsync("sql infrastructure", TestMethods.Placeholder);

        Assert.That(Proto.Context.SqlConnection().State, Is.EqualTo(ConnectionState.Open));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddSql_WithDeclaredAddressKeys_WhenNoneIsProvided_ShouldNotFailTheRunForUndeclaredApplications()
    {
        using var database = new SqliteKeeper($"sql-guard-{Guid.NewGuid():N}");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", _ => { });
        builder.AddSql(
            _ => new SqliteConnection(database.ConnectionString),
            sql => sql.AddressKeys.Add(AddressKey));
        await using var host = builder.Build();

        // The isolation guard would reject the undeclared 'Api' application at run start if the
        // integration were live; an inert integration opens no transaction to protect.
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.False);
    }

    [Test]
    public async Task AddSql_WithoutAddressKeys_ShouldKeepTheStoreCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddSql(_ => new SqliteConnection("Data Source=:memory:"));

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Store, "SQL"),
            Is.True,
            "without declared address keys the capability stays unconditional");
    }

    [Test]
    public async Task AddSql_WhenConfigurationInjectsAddressKeys_ShouldNotBindThem()
    {
        using var database = new SqliteKeeper($"sql-not-bound-{Guid.NewGuid():N}");
        const string injectedKey = "ConnectionStrings:Injected";
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Sql:AddressKeys:0"] = injectedKey
            }));
        builder.AddSql(_ => new SqliteConnection(database.ConnectionString));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("sql address keys not bound", TestMethods.Placeholder);

        var options = Proto.Context.Service<SqlOptions>();
        var connection = Proto.Context.SqlConnection();

        Assert.Multiple(() =>
        {
            Assert.That(options.AddressKeys.Count, Is.Zero,
                "configuration cannot add a key to the code-declared address rule");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.True,
                "without code-declared keys the capability stays unconditional");
            Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddSql_WithDeclaredAddressKeys_WhenConfigurationInjectsMore_ShouldFollowTheDeclaredKeys()
    {
        using var database = new SqliteKeeper($"sql-injected-{Guid.NewGuid():N}");
        const string injectedKey = "ConnectionStrings:Injected";
        DbConnection? created = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Sql:AddressKeys:0"] = injectedKey,
                [injectedKey] = database.ConnectionString
            }));
        builder.AddSql(
            _ => created = new SqliteConnection(database.ConnectionString),
            sql => sql.AddressKeys.Add(AddressKey));
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StartTestAsync("sql injected keys ignored", TestMethods.Placeholder);

        var options = Proto.Context.Service<SqlOptions>();
        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.SqlConnection());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(options.AddressKeys.ToArray(), Is.EqualTo(new[] { AddressKey }),
                "the runtime keys are exactly the code-declared keys");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.False,
                "the build decision saw only the declared key, which configuration does not provide");
            Assert.That(created, Is.Null, "an injected configuration key cannot wake an inert integration");
            Assert.That(exception!.Message, Does.Contain(AddressKey));
            Assert.That(exception.Message, Does.Not.Contain(injectedKey));
        });
    }

    [Test]
    public void SqlAddressKeys_Add_ShouldIgnoreDuplicatesAndRejectBlankKeys()
    {
        var keys = new SqlAddressKeys();

        keys.Add("ConnectionStrings:Orders", "ConnectionStrings:Orders", "ConnectionStrings:Billing");

        Assert.Multiple(() =>
        {
            Assert.That(keys.Count, Is.EqualTo(2));
            Assert.That(
                keys.ToArray(),
                Is.EqualTo(new[] { "ConnectionStrings:Orders", "ConnectionStrings:Billing" }));
            Assert.Throws<ArgumentException>(() => keys.Add(" "));
        });
    }
}
