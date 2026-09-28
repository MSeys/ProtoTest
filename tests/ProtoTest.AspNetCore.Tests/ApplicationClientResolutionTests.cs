namespace ProtoTest.AspNetCore.Tests;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest;

/// <summary>
/// Cross-application client resolution through the real host builder: a bare name resolves a unique
/// client registered under another application, the qualified form is exact, a name two applications
/// share fails naming both candidates, and an unnamed accessor keeps the ambient application's default.
/// </summary>
[TestFixture]
public sealed class ApplicationClientResolutionTests
{
    private const string ApiApplication = "Csms";
    private const string DashboardApplication = "Dashboard";
    private const string PortalApplication = "Portal";

    [Test]
    public async Task Rest_BareClientName_ShouldResolveAUniqueClientFromAnotherApplication()
    {
        await using var host = DistinctNamesHost();
        var context = await host.StartTestAsync(
            "unique cross-application client",
            TestMethods.Placeholder,
            [new ApplicationAttribute(DashboardApplication)]);

        try
        {
            var resolution = ProtoHttpClientResolver.Resolve(context, "Rest", "Api");

            Assert.Multiple(() =>
            {
                Assert.That(resolution.RequestedName, Is.EqualTo("Api"));
                Assert.That(resolution.ResolvedName, Is.EqualTo("Csms:Api"));
                Assert.That(
                    resolution.Client.BaseAddress,
                    Is.EqualTo(new Uri("http://csms.test/")),
                    "the request reaches the client registered under the other application");
                Assert.That(
                    resolution.ApplicationName,
                    Is.EqualTo(ApiApplication),
                    "the client's own application owns the address and the transport fallback");
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);

        }
    }

    [Test]
    public async Task Rest_Unnamed_ShouldKeepTheAmbientApplicationsClient()
    {
        await using var host = DistinctNamesHost();
        var context = await host.StartTestAsync(
            "ambient default",
            TestMethods.Placeholder,
            [new ApplicationAttribute(DashboardApplication)]);

        try
        {
            var resolution = ProtoHttpClientResolver.Resolve(context, "Rest");

            Assert.Multiple(() =>
            {
                Assert.That(resolution.ResolvedName, Is.EqualTo("Dashboard:DashboardApi"));
                Assert.That(
                    resolution.Client.BaseAddress,
                    Is.EqualTo(new Uri("http://dashboard.test/")),
                    "the selected application's first client is the unnamed default");
                Assert.That(resolution.ApplicationName, Is.EqualTo(DashboardApplication));
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);

        }
    }

    [Test]
    public async Task Rest_AmbiguousBareName_ShouldFailNamingBothQualifiedCandidates()
    {
        await using var host = SharedNameHost();
        var context = await host.StartTestAsync(
            "ambiguous client name",
            TestMethods.Placeholder,
            [new ApplicationAttribute(PortalApplication)]);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => context.Rest("Api"));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("'Api'"));
                Assert.That(exception.Message, Does.Contain("'Csms:Api'"));
                Assert.That(exception.Message, Does.Contain("'Dashboard:Api'"));
                Assert.That(exception.Message, Does.Contain("Qualify the name"));
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);

        }
    }

    [Test]
    public async Task Rest_AmbientDefault_ShouldWinWhenTwoApplicationsShareTheClientName()
    {
        await using var host = SharedNameHost();
        var context = await host.StartTestAsync(
            "ambient default with a collision",
            TestMethods.Placeholder,
            [new ApplicationAttribute(DashboardApplication)]);

        try
        {
            var unnamed = ProtoHttpClientResolver.Resolve(context, "Rest");
            var named = ProtoHttpClientResolver.Resolve(context, "Rest", "Api");

            Assert.Multiple(() =>
            {
                Assert.That(
                    unnamed.Client.BaseAddress,
                    Is.EqualTo(new Uri("http://dashboard.test/")),
                    "the selected application's default wins before the host-wide lookup");
                Assert.That(named.ResolvedName, Is.EqualTo("Dashboard:Api"));
                Assert.That(named.Client, Is.SameAs(unnamed.Client));
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);

        }
    }

    [Test]
    public async Task Rest_QualifiedName_ShouldResolveTheNamedApplicationExactly()
    {
        await using var host = SharedNameHost();
        var context = await host.StartTestAsync(
            "qualified client name",
            TestMethods.Placeholder,
            [new ApplicationAttribute(PortalApplication)]);

        try
        {
            var csms = ProtoHttpClientResolver.Resolve(context, "Rest", "Csms:Api");
            var dashboard = ProtoHttpClientResolver.Resolve(context, "Rest", "Dashboard:Api");

            Assert.Multiple(() =>
            {
                Assert.That(csms.ResolvedName, Is.EqualTo("Csms:Api"));
                Assert.That(csms.Client.BaseAddress, Is.EqualTo(new Uri("http://csms.test/")));
                Assert.That(csms.ApplicationName, Is.EqualTo(ApiApplication));
                Assert.That(dashboard.ResolvedName, Is.EqualTo("Dashboard:Api"));
                Assert.That(dashboard.Client.BaseAddress, Is.EqualTo(new Uri("http://dashboard.test/")));
                Assert.That(dashboard.ApplicationName, Is.EqualTo(DashboardApplication));
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);

        }
    }

    [Test]
    public async Task Rest_MissingName_ShouldListTheRegisteredCandidates()
    {
        await using var host = SharedNameHost();
        var context = await host.StartTestAsync(
            "missing client name",
            TestMethods.Placeholder,
            [new ApplicationAttribute(PortalApplication)]);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => context.Rest("Missing"));

            Assert.Multiple(() =>
            {
                Assert.That(
                    exception!.Message,
                    Does.Contain("No HTTP client 'Portal:Missing' is registered."));
                Assert.That(exception.Message, Does.Contain("Registered for this protocol:"));
                Assert.That(exception.Message, Does.Contain("'Csms:Api'"));
                Assert.That(exception.Message, Does.Contain("'Dashboard:Api'"));
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);

        }
    }

    [Test]
    public async Task Rest_ShouldResolveCrossApplicationClientsInParallel()
    {
        await using var host = DistinctNamesHost();

        var resolutions = await Task.WhenAll(ResolveAsync("Api"), ResolveAsync("DashboardApi"));

        Assert.Multiple(() =>
        {
            Assert.That(resolutions[0].ResolvedName, Is.EqualTo("Csms:Api"));
            Assert.That(resolutions[0].Client.BaseAddress, Is.EqualTo(new Uri("http://csms.test/")));
            Assert.That(resolutions[1].ResolvedName, Is.EqualTo("Dashboard:DashboardApi"));
            Assert.That(
                resolutions[1].Client.BaseAddress,
                Is.EqualTo(new Uri("http://dashboard.test/")),
                "each parallel context resolves its own client");
        });

        async Task<ProtoHttpClientResolution> ResolveAsync(string clientName)
            => await Task.Run(async () =>
            {
                var context = await host.StartTestAsync(
                    $"parallel {clientName}",
                    TestMethods.Placeholder,
                    [new ApplicationAttribute(PortalApplication)]);
                try
                {
                    return ProtoHttpClientResolver.Resolve(context, "Rest", clientName);
                }
                finally
                {
                    await host.CompleteTestAsync(ProtoTestResult.Passed);
                }
            });
    }

    private static ProtoHost DistinctNamesHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication(PortalApplication, _ => { });
        builder.AddApplication(ApiApplication, app => app.AddRest(rest => rest.AddClient("Api", "http://csms.test/")));
        builder.AddApplication(DashboardApplication, app => app.AddRest(
            rest => rest.AddClient("DashboardApi", "http://dashboard.test/")));
        return builder.Build();
    }

    private static ProtoHost SharedNameHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication(PortalApplication, _ => { });
        builder.AddApplication(ApiApplication, app => app.AddRest(rest => rest.AddClient("Api", "http://csms.test/")));
        builder.AddApplication(DashboardApplication, app => app.AddRest(
            rest => rest.AddClient("Api", "http://dashboard.test/")));
        return builder.Build();
    }
}
