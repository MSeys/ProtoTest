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
    public async Task SeleniumOptions_ShouldBindTheBackendSectionFromConfiguration()
    {
        var publisher = new RecordingAttachmentPublisher();
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Selenium:DiagnosticTraceRetention"] = "Always"
                }))
            .AddWeb(() => new StubWebDriver())
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder, attachmentPublisher: publisher);
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var names = publisher.Attachments.Select(item => item.Name).ToArray();
        Assert.That(names, Has.Some.EndsWith("selenium-default-diagnostics.json"));
    }

    [Test]
    public async Task Options_ShouldApplyCodeThenBackendConfiguration()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Playwright:Browser"] = "Firefox",
                ["ProtoTest:Web:Playwright:Channel"] = "msedge",
                ["ProtoTest:Web:Playwright:Locale"] = "nl-BE",
                ["ProtoTest:Web:Playwright:ViewportWidth"] = "1280",
                ["ProtoTest:Web:Playwright:ViewportHeight"] = "720"
            }))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web options", TestMethods.Placeholder);

        var resolved = WebBackendOptions.Resolve<ProtoTest.Web.Playwright.PlaywrightWebOptions>(
            context,
            options =>
            {
                options.Headless = false;
                options.Channel = "chrome";
                options.SlowMo = 10;
            });

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Headless, Is.False, "code value without configuration is kept");
            Assert.That(resolved.SlowMo, Is.EqualTo(10));
            Assert.That(resolved.Browser, Is.EqualTo(ProtoTest.Web.Playwright.PlaywrightBrowser.Firefox));
            Assert.That(resolved.Channel, Is.EqualTo("msedge"), "the backend section wins over code");
            Assert.That(resolved.Locale, Is.EqualTo("nl-BE"));
            Assert.That(resolved.ViewportWidth, Is.EqualTo(1280));
            Assert.That(resolved.ViewportHeight, Is.EqualTo(720));
            var contextOptions = resolved.BuildContextOptions();
            Assert.That(contextOptions.Locale, Is.EqualTo("nl-BE"));
            Assert.That(contextOptions.ViewportSize?.Width, Is.EqualTo(1280));
            Assert.That(contextOptions.ViewportSize?.Height, Is.EqualTo(720));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Options_ShouldValidateTheBoundResult()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:00"
            }))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web options", TestMethods.Placeholder);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WebBackendOptions.Resolve<SeleniumWebOptions>(context, validate: SeleniumWebOptions.Validate),
                "the bound result is validated");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WebBackendOptions.Resolve<SeleniumWebOptions>(
                    context,
                    configure: options => options.PollInterval = TimeSpan.Zero,
                    validate: SeleniumWebOptions.Validate),
                "code configuration that survives binding is validated");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WebBackendOptions.Resolve<ProtoTest.Web.Playwright.PlaywrightWebOptions>(
                    context,
                    configure: options => options.ActionTimeout = TimeSpan.Zero,
                    validate: ProtoTest.Web.Playwright.PlaywrightWebOptions.Validate),
                "Playwright's action timeout is validated the same way");
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

}
