namespace ProtoTest.Sql.Tests;

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;

/// <summary>
/// Pins the Entity Framework Core adapter's address rule: it shares
/// <see cref="SqlOptions.AddressKeys"/> with SQL, so an inert SQL run drops the
/// <c>Entity Framework Core</c> store capability, the enlistment hook stays out of the context and the
/// session, and gated tests skip; with the address provided the enlistment and rollback are unchanged.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SqlEntityFrameworkCoreAddressTests
{
    private const string AddressKey = "ConnectionStrings:Csms";
    private const string CapabilityName = "Entity Framework Core";

    private SqliteKeeper _database = null!;

    [SetUp]
    public void SetUp() => _database = new SqliteKeeper($"sqlefaddress-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task AddEntityFrameworkCore_WithDeclaredAddressKeysUnprovided_ShouldDropTheCapabilityAndStayInert()
    {
        DbConnection? created = null;
        var contextConfigured = false;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddSql(
            _ => created = new SqliteConnection(_database.ConnectionString),
            sql => sql.AddressKeys.Add(AddressKey));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => contextConfigured = true);

        await using var host = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.False);
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Store, CapabilityName),
                Is.False,
                "the Entity Framework Core capability follows the SQL address keys");
            Assert.That(
                new RequiresCapabilityAttribute(ProtoCapabilityKinds.Store) { CapabilityName = CapabilityName }
                    .GetSkipReason(host),
                Is.Not.Null,
                "a gated test skips instead of failing at setup");
        });

        await host.StartAsync();
        await host.StartTestAsync("ef inert", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.Sql<WidgetDbContext>());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.Null, "the connection factory is not called during an inert setup");
            Assert.That(contextConfigured, Is.False, "the inert hook never builds the context");
            Assert.That(exception!.Message, Does.Contain(AddressKey));
            Assert.That(exception.Message, Does.Contain("RequiresCapability(ProtoCapabilityKinds.Store)"));
        });
    }

    [Test]
    public async Task AddEntityFrameworkCore_WithDeclaredAddressKeysProvided_ShouldEnlistAndRollBack()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [AddressKey] = _database.ConnectionString
            }));
        builder.AddSql(
            _ => new SqliteConnection(_database.ConnectionString),
            sql => sql.AddressKeys.Add(AddressKey));
        builder.AddEntityFrameworkCore<WidgetDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));

        await using var host = builder.Build();
        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Store, CapabilityName),
            Is.True,
            "a provided key keeps both store capabilities");

        await host.StartAsync();
        await host.StartTestAsync("ef configured", TestMethods.Placeholder);

        var context = Proto.Context.Sql<WidgetDbContext>();
        context.Widgets.Add(new Widget { Name = "gear" });
        await context.SaveChangesAsync();
        Assert.That(await context.Widgets.CountAsync(), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(
            SqliteKeeper.CountWidgets(_database.Connection),
            Is.Zero,
            "the context enlists in the test transaction and rolls back as before");
        await host.StopAsync();
    }

    [Test]
    public async Task AddEntityFrameworkCore_WithoutAddressKeys_ShouldKeepItsCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));

        await using var host = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Store, "SQL"), Is.True);
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Store, CapabilityName),
                Is.True,
                "without declared address keys the Entity Framework Core capability stays unconditional");
        });
    }
}
