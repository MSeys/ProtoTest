namespace ProtoTest.Web.Tests;

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Web.Internal;
using ProtoTest.Web.Playwright;
using ProtoTest.Web.Selenium;

public sealed partial class WebModelTests
{
    [Test]
    public async Task Assertion_ShouldReadTheVerifiedAddressInsideTheAssertionOperation()
    {
        // Audit 5 A5.9 (C07): coverage comes from the address the assertion operation itself read, not
        // from a second untraced backend call after the operation completed.
        var factory = new FakeBackendFactory();
        factory.Backend.CurrentAddress = "https://example.test/login";
        factory.Backend.TextResults.Enqueue("ready");
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web address inside", TestMethods.Placeholder);
        var form = context.Web().Page<LoginPage>().Form;

        await form.Status.Should.HaveTextAsync("ready", TimeSpan.FromSeconds(1));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var backendCalls = factory.Backend.CallOrder
            .Where(item => item is "begin" or "address" or "end")
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(backendCalls, Is.EqualTo(new[] { "begin", "address", "end" }),
                "the address read happens inside the assertion operation");
            Assert.That(context.RecordedObservations.Count(item => item.Kind == "web.page.verified"),
                Is.EqualTo(1));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries.Count(item => item.Kind == "web.backend.execute"),
                Is.EqualTo(1), "one backend operation per assertion");
        });
    }

    [Test]
    public async Task SeleniumPollInterval_ShouldReachTheSessionAssertions()
    {
        // Audit 5 A5.12 (C11): the session assertion poller reads the backend's interval, so Selenium's
        // PollInterval retimes element assertions as well as the backend's own action retries.
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder()
            .AddWeb(() => driver, options => options.PollInterval = TimeSpan.FromMilliseconds(100))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium interval", TestMethods.Placeholder);

        var exception = Assert.ThrowsAsync<WebAssertionException>(async () =>
            await context.Web().Page<LoginPage>().Form.Status.Should.BeVisibleAsync(
                TimeSpan.FromMilliseconds(500)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(driver.FindAllQueries.Count, Is.LessThanOrEqualTo(16),
            "a 500ms assertion at the backend's 100ms interval probes a handful of times, not the ~40 lookups the 25ms default produces");
    }

    [Test]
    public async Task SeleniumRelease_ShouldTraceWorkAbandonedOnThePump()
    {
        // Audit 5 A5.9 (C05): a pump stuck in a driver call no longer makes release silent - the bound
        // expires, the in-flight work is named, and release does not hang.
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var driver = new StubWebDriver
        {
            Elements = _ =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return [new FakeElement("text")];
            }
        };
        var host = new ProtoHostBuilder().Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium abandoned pump", TestMethods.Placeholder);
        var backend = new SeleniumWebBackend(
            context,
            driver,
            new SeleniumWebOptions(),
            "Default",
            TimeSpan.FromMilliseconds(100));
        var reference = new WebElementReference([], "Page", "Row", By.TestId("row"));

        var stuck = backend.CountAsync(reference).AsTask();
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True, "the pump reached the stuck driver call");
        await backend.DisposeAsync();
        release.Set();
        await stuck;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "web.selenium.executor_abandoned");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["web.executor.work"], Is.EqualTo(nameof(SeleniumWebBackend.CountAsync)));
            Assert.That(SpinWait.SpinUntil(() => driver.QuitCalled, TimeSpan.FromSeconds(5)), Is.True,
                "the queued cleanup still ran once the pump was free");
        });
    }
}
