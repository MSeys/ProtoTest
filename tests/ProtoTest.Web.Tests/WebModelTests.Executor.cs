namespace ProtoTest.Web.Tests;

using ProtoTest.Core;
using ProtoTest.Web.Selenium;

public sealed partial class WebModelTests
{
    [Test]
    public async Task SeleniumDriver_ShouldRunEveryCallOnItsPumpThread()
    {
        var driver = new StubWebDriver { Elements = _ => [new FakeElement("text")] };
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium executor thread", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();
        var reference = new WebElementReference([], "Page", "Row", By.TestId("row"));

        await backend.CountAsync(reference);
        await backend.ReadTextAsync(reference);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(driver.CallerThreads, Has.Count.EqualTo(1), "every driver call runs on the one pump thread");
            Assert.That(driver.CallerThreads.Single(), Is.Not.EqualTo(Environment.CurrentManagedThreadId),
                "the pump is not the test's thread");
        });
    }

    [Test]
    public async Task SeleniumDriver_ShouldSerializeConcurrentOperationsOnThePump()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var driver = new StubWebDriver
        {
            Elements = _ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                }

                return [new FakeElement("text")];
            }
        };
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium executor serialization", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();
        var reference = new WebElementReference([], "Page", "Row", By.TestId("row"));

        var first = backend.CountAsync(reference).AsTask();
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True, "the first call reached the driver");
        var second = backend.CountAsync(reference).AsTask();
        // A deliberate timing probe: the blocked second call has no observable signal of its own, so a
        // short window is how a race against the serialization pump would show up.
        await Task.Delay(50);
        Assert.That(calls, Is.EqualTo(1), "the second call waits for the pump instead of racing the driver");
        release.Set();

        Assert.That(await first, Is.EqualTo(1));
        Assert.That(await second, Is.EqualTo(1));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(calls, Is.EqualTo(2));
    }
}
