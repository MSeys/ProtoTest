namespace ProtoTest.Web.Tests;

using System.Reflection;
using ProtoTest.Core;
using ProtoTest.Web.Playwright;

[TestFixture]
public sealed class WebSessionConfigurationTests
{
    [Test]
    public async Task SessionBaseUrl_ShouldComeFromSettingsInfrastructure()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWeb(options => options.InstallBrowsers = true);
        builder.AddInfrastructure(new FakeSettingsInfrastructure());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("web base url", TestMethod());

        var web = context.Web();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(web.BaseUrl, Is.EqualTo(new Uri("http://standalone.test:8080/")),
            "Started infrastructure fills the session's base URL.");
    }

    private sealed class FakeSettingsInfrastructure : IProtoSettingsInfrastructure
    {
        public string Id => "application:fake";

        public string Kind => "application";

        public string Description => "Fake standalone application";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string>
        {
            ["ProtoTest:Web:Sessions:Default:BaseUrl"] = "http://standalone.test:8080/"
        };

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private static MethodInfo TestMethod()
        => typeof(WebSessionConfigurationTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
