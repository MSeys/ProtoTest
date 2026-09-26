namespace ProtoTest.Web.Tests;

using ProtoTest.Core;
using ProtoTest.Web.Selenium;

/// <summary>
/// The failure path Audit 5 C-01 named: a click or option selection Selenium accepts without the page
/// changing state. The stub driver's element never changes <c>Selected</c> unless a test's click handler
/// does, so "the click did not take" is a modelled state rather than a timing guess.
/// </summary>
public sealed partial class WebModelTests
{
    [Test]
    public async Task SeleniumCheck_ShouldFailWhenTheClickDoesNotTake()
    {
        var checkbox = new FakeElement("newsletter");
        var host = SeleniumHost(checkbox);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium swallowed check", TestMethods.Placeholder);

        var exception = Assert.ThrowsAsync<WebActionabilityException>(async () =>
            await context.Web().Page<LoginPage>().Form.RememberMe.CheckAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("did not become actionable"));
            Assert.That(exception.Message, Does.Contain("checked=false after the click"),
                "the last observation names the state that did not change");
            Assert.That(checkbox.Selected, Is.False);
        });
    }

    [Test]
    public async Task SeleniumCheck_ShouldSucceedWhenTheClickTakes()
    {
        var checkbox = new FakeElement("newsletter");
        checkbox.OnClick = () => checkbox.Selected = !checkbox.Selected;
        var host = SeleniumHost(checkbox);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium effective check", TestMethods.Placeholder);

        await context.Web().Page<LoginPage>().Form.RememberMe.CheckAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(checkbox.Selected, Is.True, "the verified click changed the state");
    }

    [Test]
    public async Task SeleniumSelectOption_ShouldFailWhenTheOptionDoesNotBecomeSelected()
    {
        var option = new FakeElement("Nederlands") { DomValue = "nl" };
        var select = new FakeElement("Language");
        select.Options = [option];
        var host = SeleniumHost(select);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium swallowed select", TestMethods.Placeholder);

        var exception = Assert.ThrowsAsync<WebActionabilityException>(async () =>
            await context.Web().Page<LoginPage>().Form.Language.SelectOptionAsync("nl"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("did not become actionable"));
            Assert.That(exception.Message, Does.Contain("option with value 'nl' is not selected after the click"),
                "the last observation names the option that did not take");
            Assert.That(option.Selected, Is.False);
        });
    }

    [Test]
    public async Task SeleniumSelectOption_ShouldSucceedWhenTheOptionBecomesSelectedAfterTheClick()
    {
        var option = new FakeElement("Nederlands") { DomValue = "nl" };
        option.OnClick = () => option.Selected = true;
        var select = new FakeElement("Language");
        select.Options = [option];
        var host = SeleniumHost(select);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium effective select", TestMethods.Placeholder);

        await context.Web().Page<LoginPage>().Form.Language.SelectOptionAsync("nl");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(option.Selected, Is.True, "the verified selection took");
    }

    private static ProtoHost SeleniumHost(FakeElement element)
        => new ProtoHostBuilder()
            .AddWeb(
                () => new StubWebDriver { Elements = _ => [element] },
                options =>
                {
                    options.ActionTimeout = TimeSpan.FromMilliseconds(300);
                    options.WaitForStableBounds = false;
                })
            .Build();
}
