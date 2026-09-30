namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;

/// <summary>
/// The consumer redaction seam: a name configured on the builder redacts in state values and
/// finding metadata on that host, the defaults keep working, and a host without the configuration
/// records the name verbatim.
/// </summary>
[TestFixture]
public sealed class ProtoRedactionOptionsTests
{
    private sealed record OwnerState(string Tenant, string OwnerToken) : IProtoContext;

    [Test]
    public async Task ConfigureRedaction_ShouldRedactTheConfiguredNameInStateAndFindings()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureRedaction(redaction => redaction.AddSensitiveName("OwnerToken"));
        await using var host = builder.Build();
        await host.StartAsync();

        var context = await host.StartTestAsync("redaction", "00070", TestMethods.Placeholder);
        context.SetContext(new OwnerState("tenant-1", "owner-secret"));
        context.AddFinding(
            "Owner metadata.",
            metadata: new Dictionary<string, object>
            {
                ["OwnerToken"] = "owner-secret",
                ["token"] = "default-secret"
            });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var test = host.Trace.Snapshot().Tests.Single();
        var entity = test.Entities!.Single(item =>
            item.Kind == ProtoTraceEntityKinds.Context && item.Id.Contains(nameof(OwnerState)));
        var finding = test.Record!.Findings!.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.State["context.value"], Does.Contain("[REDACTED]"));
            Assert.That(entity.State["context.value"], Does.Not.Contain("owner-secret"));
            Assert.That(finding.Metadata!["OwnerToken"], Is.EqualTo("[REDACTED]"));
            Assert.That(
                finding.Metadata!["token"], Is.EqualTo("[REDACTED]"), "the defaults keep working");
        }
    }

    [Test]
    public async Task ConfigureRedaction_ShouldStayOnTheHostThatConfiguredIt()
    {
        var configuredBuilder = new ProtoHostBuilder();
        configuredBuilder.ConfigureRedaction(redaction => redaction.AddSensitiveName("OwnerToken"));
        await using var configuredHost = configuredBuilder.Build();
        await using var plainHost = new ProtoHostBuilder().Build();
        await configuredHost.StartAsync();
        await plainHost.StartAsync();

        var configuredContext =
            await configuredHost.StartTestAsync("redaction", "00071", TestMethods.Placeholder);
        configuredContext.AddFinding(
            "Owner metadata.", metadata: new Dictionary<string, object> { ["OwnerToken"] = "owner-secret" });
        await configuredHost.CompleteTestAsync(ProtoTestResult.Passed);

        var plainContext =
            await plainHost.StartTestAsync("redaction", "00071", TestMethods.Placeholder);
        plainContext.AddFinding(
            "Owner metadata.", metadata: new Dictionary<string, object> { ["OwnerToken"] = "owner-secret" });
        await plainHost.CompleteTestAsync(ProtoTestResult.Passed);

        await configuredHost.StopAsync();
        await plainHost.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                configuredHost.Trace.Snapshot().Tests.Single().Record!.Findings!.Single().Metadata!["OwnerToken"],
                Is.EqualTo("[REDACTED]"));
            Assert.That(
                plainHost.Trace.Snapshot().Tests.Single().Record!.Findings!.Single().Metadata!["OwnerToken"],
                Is.EqualTo("owner-secret"),
                "a host without the configuration records the name verbatim");
        }
    }

    [Test]
    public async Task ConfigureRedaction_ShouldBindAdditionalNamesFromConfiguration()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(settings => settings.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProtoTest:Redaction:AdditionalSensitiveNames:0"] = "OwnerToken"
        }));
        await using var host = builder.Build();
        await host.StartAsync();

        var context = await host.StartTestAsync("redaction", "00072", TestMethods.Placeholder);
        context.AddFinding(
            "Owner metadata.", metadata: new Dictionary<string, object> { ["OwnerToken"] = "owner-secret" });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(
            host.Trace.Snapshot().Tests.Single().Record!.Findings!.Single().Metadata!["OwnerToken"],
            Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void AddSensitiveName_ShouldRejectABlankName()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(() => new ProtoRedactionOptions().AddSensitiveName("  "));
            Assert.Throws<ArgumentNullException>(() => new ProtoRedactionOptions().AddSensitiveName(null!));
        }
    }
}
