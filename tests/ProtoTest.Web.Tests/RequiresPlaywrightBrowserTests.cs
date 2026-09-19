namespace ProtoTest.Web.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Web.Playwright;

[TestFixture]
public sealed class RequiresPlaywrightBrowserTests
{
    // Compile-time guard: every documented form, including Session, stays a valid attribute usage.
    [RequiresPlaywrightBrowser]
    [RequiresPlaywrightBrowser(PlaywrightBrowser.Firefox)]
    [RequiresPlaywrightBrowser(browser: PlaywrightBrowser.Firefox)]
    [RequiresPlaywrightBrowser(channel: "msedge", Reason = "No Edge in this environment.")]
    [RequiresPlaywrightBrowser(Session = "Admin")]
    private sealed class SessionAttributeSyntax;

    [Test]
    public void MissingBrowserReason_ShouldNamePlaywrightAndTheInstallOptions()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"prototest-missing-{Guid.NewGuid():N}", "chrome.exe");

        var reason = PlaywrightBrowserProbe.MissingBrowserReason(PlaywrightBrowser.Chromium, missing);

        Assert.Multiple(() =>
        {
            Assert.That(reason, Is.Not.Null);
            Assert.That(reason, Does.Contain("Playwright").And.Contain("chromium"));
            Assert.That(reason, Does.Contain("InstallBrowsers"));
            Assert.That(reason, Does.Contain("playwright.ps1 install chromium"));
        });
    }

    [Test]
    public void MissingBrowserReason_ShouldNotSkipWhenTheExecutableExists()
    {
        var existing = typeof(RequiresPlaywrightBrowserTests).Assembly.Location;

        var reason = PlaywrightBrowserProbe.MissingBrowserReason(PlaywrightBrowser.Chromium, existing);

        Assert.That(reason, Is.Null);
    }

    [Test]
    public async Task Attribute_ShouldNotSkipWhenInstallBrowsersIsConfiguredInCode()
    {
        await using var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.InstallBrowsers = true;
                options.Browser = PlaywrightBrowser.Webkit;
            })
            .Build();

        var reason = new RequiresPlaywrightBrowserAttribute().GetSkipReason(host);

        Assert.That(reason, Is.Null, "InstallBrowsers makes the pool download the browser on demand.");
    }

    [Test]
    public async Task Attribute_ShouldNotSkipWhenInstallBrowsersIsConfigured()
    {
        await using var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Playwright:InstallBrowsers"] = "true",
                    ["ProtoTest:Web:Playwright:Browser"] = "Webkit"
                }))
            .Build();

        var reason = new RequiresPlaywrightBrowserAttribute().GetSkipReason(host);

        Assert.That(reason, Is.Null, "InstallBrowsers makes the pool download the browser on demand.");
    }

    [Test]
    public async Task Attribute_ShouldSeeTheCodeDefaultsOfAnApplicationRegistration()
    {
        await using var host = new ProtoHostBuilder()
            .AddApplication("Api", app => app.AddWeb(options => options.Channel = "prototest-no-such-channel"))
            .Build();
        await host.StartAsync();

        var reason = new RequiresPlaywrightBrowserAttribute().GetSkipReason(host);

        Assert.That(reason, Does.Contain("prototest-no-such-channel"),
            "the application's code-configured channel reaches the probe");
    }

    [Test]
    public async Task Attribute_ShouldPreferConfigurationOverTheCodeDefaultsOfAnApplicationRegistration()
    {
        await using var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Playwright:Channel"] = "msedge"
                }))
            .AddApplication("Api", app => app.AddWeb(options => options.Channel = "prototest-no-such-channel"))
            .Build();
        await host.StartAsync();

        var reason = new RequiresPlaywrightBrowserAttribute().GetSkipReason(host);

        // Whether msedge exists is environment-specific; what is not is that the code default lost.
        Assert.That(reason ?? string.Empty, Does.Not.Contain("prototest-no-such-channel"),
            "explicit configuration wins over the application's code defaults");
    }

    [Test]
    public async Task Attribute_ShouldApplyASessionLevelInstallBrowsersOverTheProtocolSection()
    {
        await using var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Playwright:Channel"] = "prototest-no-such-channel",
                    ["ProtoTest:Web:Sessions:Default:InstallBrowsers"] = "true"
                }))
            .Build();

        var reason = new RequiresPlaywrightBrowserAttribute { Session = "Default" }.GetSkipReason(host);

        Assert.That(reason, Is.Null, "the session's InstallBrowsers overrides the protocol section");
    }

    [Test]
    public async Task Attribute_ShouldApplyASessionLevelChannelOverTheProtocolSection()
    {
        await using var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Playwright:Channel"] = "prototest-no-such-channel",
                    ["ProtoTest:Web:Sessions:Default:Channel"] = "msedge"
                }))
            .Build();

        var reason = new RequiresPlaywrightBrowserAttribute { Session = "Default" }.GetSkipReason(host);

        // Whether msedge exists is environment-specific; the protocol section's unknown channel losing is not.
        Assert.That(reason ?? string.Empty, Does.Not.Contain("prototest-no-such-channel"),
            "the session's channel wins over the protocol section");
    }

    [Test]
    public async Task Attribute_ShouldReportAnUnknownSessionLevelChannel()
    {
        await using var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Sessions:Default:Channel"] = "prototest-no-such-channel"
                }))
            .Build();

        var reason = new RequiresPlaywrightBrowserAttribute { Session = "Default" }.GetSkipReason(host);

        Assert.Multiple(() =>
        {
            Assert.That(reason, Is.Not.Null);
            Assert.That(reason, Does.Contain("prototest-no-such-channel"),
                "the probe follows the session's channel, not the protocol default");
        });
    }

    [Test]
    public async Task Attribute_ShouldFollowWhetherChromiumIsInstalled()
    {
        // The expected verdict comes from the machine, not from the test host: a browser-less CI image
        // must expect the skip reason instead of failing.
        var executable = PlaywrightBrowserProbe.ExecutablePath(PlaywrightBrowser.Chromium);
        await using var host = new ProtoHostBuilder().Build();

        var reason = new RequiresPlaywrightBrowserAttribute().GetSkipReason(host);

        if (executable is not null && File.Exists(executable))
        {
            Assert.That(reason, Is.Null, "an installed Chromium produces no skip");
        }
        else
        {
            Assert.That(reason, Is.Not.Null.And.Contains("chromium"),
                "a missing Chromium produces a skip reason naming it");
        }
    }

    [Test]
    public async Task Attribute_ShouldFollowAKnownSystemChannel()
    {
        await using var host = new ProtoHostBuilder().Build();

        var reason = new RequiresPlaywrightBrowserAttribute(channel: "msedge").GetSkipReason(host);

        if (PlaywrightBrowserProbe.ChannelIsKnown("msedge"))
        {
            Assert.That(reason, Is.Null,
                "a recognizable channel names a system browser, which only a launch can resolve");
        }
        else
        {
            Assert.That(reason, Is.Not.Null.And.Contains("msedge"));
        }
    }

    [Test]
    public async Task Attribute_ShouldSkipWithAReasonWhenTheChannelIsUnknown()
    {
        await using var host = new ProtoHostBuilder().Build();

        var reason = new RequiresPlaywrightBrowserAttribute(channel: "prototest-no-such-channel").GetSkipReason(host);

        Assert.Multiple(() =>
        {
            Assert.That(reason, Is.Not.Null);
            Assert.That(reason, Does.Contain("Playwright").And.Contain("prototest-no-such-channel"));
        });
    }

    [Test]
    public async Task Attribute_ShouldUseTheConfiguredReason()
    {
        await using var host = new ProtoHostBuilder().Build();
        var attribute = new RequiresPlaywrightBrowserAttribute(channel: "prototest-no-such-channel")
        {
            Reason = "No browser in this environment."
        };

        var reason = attribute.GetSkipReason(host);

        Assert.That(reason, Is.EqualTo("No browser in this environment."));
    }
}
