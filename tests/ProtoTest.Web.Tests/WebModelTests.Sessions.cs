namespace ProtoTest.Web.Tests;

using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Web.Internal;
using ProtoTest.Web.Playwright;
using ProtoTest.Web.Selenium;

public sealed partial class WebModelTests
{
    [Test]
    public async Task WebSession_ShouldCreateAndOpenTheDeclaredSessionDuringSetup()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethods.Placeholder,
            [
                new WebSessionAttribute("Anon"),
                new WebSessionAttribute("Admin") { Open = "https://example.test/dashboard" }
            ]);

        var admin = Proto.Context.Web("Admin");
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.Operations.Select(item => item.Kind), Does.Contain("navigate"));
            Assert.That(admin, Is.SameAs(Proto.Context.Web("Admin")));
            Assert.That(Proto.Context.Web("Anon"), Is.Not.SameAs(admin));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldResolveRelativeOpenAgainstItsApplication()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Admin:BaseUrl"] = "https://env.test"
            })));
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethods.Placeholder,
            [new WebSessionAttribute("Admin") { Open = "/back-office" }]);

        Assert.That(factory.Backend.Operations.Single().Value, Is.EqualTo("https://env.test/back-office"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldUseTheApplicationNamedOnTheAttribute()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://control.test"
            })));
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethods.Placeholder,
            [new WebSessionAttribute("Admin") { Application = "ControlPlane", Open = "/back-office" }]);

        Assert.That(factory.Backend.Operations.Single().Value, Is.EqualTo("https://control.test/back-office"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldRequireAnOriginForRelativeOpen()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.StartTestAsync(
                "web session",
                TestMethods.Placeholder,
                [new WebSessionAttribute("Admin") { Open = "/back-office" }]));

        Assert.That(exception!.Message, Does.Contain("BaseUrl"));
    }

    [Test]
    public async Task Options_ShouldPreferInfrastructureSettingsOverConfiguration()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:07",
                    ["ProtoTest:Web:Selenium:PollInterval"] = "00:00:00.250"
                }))
            .AddInfrastructure(
                "web-options",
                chain => chain.Use(new ProtoTargetProvider(
                    "settings",
                    new FakeSettingsInfrastructure(new Dictionary<string, string>
                    {
                        ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:09"
                    }))))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web options", TestMethods.Placeholder);

        var settings = context.TryService<ProtoInfrastructureSettings>();
        var options = WebBackendOptions.Resolve<SeleniumWebOptions>(context);

        Assert.Multiple(() =>
        {
            Assert.That(settings, Is.Not.Null);
            Assert.That(options.ActionTimeout, Is.EqualTo(TimeSpan.FromSeconds(9)),
                "started infrastructure overrides the static configuration for the keys it provides");
            Assert.That(options.PollInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)),
                "keys the infrastructure does not provide still come from configuration");
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private sealed class FakeSettingsInfrastructure(IReadOnlyDictionary<string, string> settings)
        : IProtoSettingsInfrastructure
    {
        public string Id => "application:test";
        public string Kind => "application";
        public string Description => "Test settings infrastructure";
        public ProtoResourceScope Scope => ProtoResourceScope.Run;
        public IReadOnlyDictionary<string, string> Settings { get; } = settings;
        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    [Test]
    public async Task Web_WithoutAnApplication_ShouldReturnTheSessionAnAttributeOpenedForAnotherApplication()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("attribute session", TestMethods.Placeholder);

        // The test selects Csms, which runs in-process, and declares its browser session on Dashboard.
        await new ApplicationAttribute("Csms").BeforeTestAsync(context);
        await new WebSessionAttribute("Default") { Application = "Dashboard" }.BeforeTestAsync(context);

        var session = context.Web();

        Assert.Multiple(() =>
        {
            Assert.That(session.Application, Is.EqualTo("Dashboard"));
            Assert.That(context.Web("Default"), Is.SameAs(session));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Web_WithASessionNameUnderTwoOtherApplications_ShouldThrowNamingBoth()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("ambiguous sessions", TestMethods.Placeholder);
        context.Web("Admin", application: "Shop");
        context.Web("Admin", application: "BackOffice");

        var exception = Assert.Throws<InvalidOperationException>(() => context.Web("Admin"));

        Assert.That(exception!.Message, Does.Contain("'BackOffice:Admin'").And.Contain("'Shop:Admin'"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Web_WithAnExplicitApplication_ShouldNeverFallBackToAnotherApplication()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("exact session", TestMethods.Placeholder);
        var backOffice = context.Web("Admin", application: "BackOffice");

        var shop = context.Web("Admin", application: "Shop");

        Assert.Multiple(() =>
        {
            Assert.That(shop, Is.Not.SameAs(backOffice));
            Assert.That(shop.Application, Is.EqualTo("Shop"));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldKeySessionsByNameAndApplication()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web sessions", TestMethods.Placeholder);

        var shop = context.Web("Admin", application: "Shop");
        var backOffice = context.Web("Admin", application: "BackOffice");

        Assert.Multiple(() =>
        {
            Assert.That(backOffice, Is.Not.SameAs(shop));
            Assert.That(context.Web("Admin", application: "Shop"), Is.SameAs(shop));
            Assert.That(shop.Application, Is.EqualTo("Shop"));
            Assert.That(backOffice.Application, Is.EqualTo("BackOffice"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldRetryBackendCreationAfterASynchronousFailure()
    {
        var attempts = 0;
        var factory = new FlakyBackendFactory((context, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("the browser is not installed");
            }

            return new FakeBackend { Context = context };
        });
        var builder = new ProtoHostBuilder();
        builder.AddWebBackend(factory);
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("web retry", TestMethods.Placeholder);
        var session = context.Web();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.GetBackendAsync<IWebBackend>());
        Assert.That(exception!.Message, Is.EqualTo("the browser is not installed"));

        // A failed creation must not be cached: the next call starts from a clean slate.
        var backend = await session.GetBackendAsync<IWebBackend>();
        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(backend.Name, Is.EqualTo("Fake"));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private sealed class FlakyBackendFactory(
        Func<ProtoExecutionContext, string, IWebBackend> create) : IWebBackendFactory
    {
        public string Name => "Flaky";

        public ValueTask<IWebBackend> CreateAsync(
            ProtoExecutionContext context,
            string sessionName,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(create(context, sessionName));
    }

    // Compile-time guard: [WebSession] must resolve to WebSessionAttribute even though WebSession is a type.
    [WebSession("Admin", Open = "/dashboard")]
    private sealed class WebSessionAttributeSyntax;

}
