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
            .AddInfrastructure(new FakeSettingsInfrastructure(new Dictionary<string, string>
            {
                ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:09"
            }))
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

    // Compile-time guard: [WebSession] must resolve to WebSessionAttribute even though WebSession is a type.
    [WebSession("Admin", Open = "/dashboard")]
    private sealed class WebSessionAttributeSyntax;

}
