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
    public async Task FormOperations_ShouldUseTheSharedOperationPipeline()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var form = context.Web().Page<LoginPage>().Form;

        await form.RememberMe.CheckAsync();
        await form.RememberMe.UncheckAsync();
        await form.Language.SelectOptionAsync("nl");
        await form.Password.PressAsync(WebKey.Enter);

        Assert.That(factory.Backend.Operations.Select(item => item.Kind), Is.EqualTo(
            new[] { "check:True", "check:False", "select", "press" }));
        Assert.That(host.Trace.Snapshot().Tests.Single().Entries.Select(item => item.Kind),
            Has.Some.EqualTo("web.select_option"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Assertions_ShouldPollAndKeepFormValuesOutOfTrace()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextResults.Enqueue("loading");
        factory.Backend.TextResults.Enqueue("ready");
        factory.Backend.ValueResult = "super-secret";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var form = context.Web().Page<LoginPage>().Form;

        await form.Status.Should.HaveTextAsync("ready", TimeSpan.FromSeconds(1));
        await form.Password.Should.HaveValueAsync("super-secret", TimeSpan.FromSeconds(1));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var serializedTrace = System.Text.Json.JsonSerializer.Serialize(host.Trace.Snapshot());
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.Operations.Count(item => item.Kind == "text"), Is.EqualTo(2));
            Assert.That(serializedTrace, Does.Not.Contain("super-secret"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries.Count(item => item.Kind == "assert.web"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task NegatedAssertions_ShouldPassWhenTheCheckDoesNotHold()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.VisibleResult = false;
        factory.Backend.EnabledResult = false;
        factory.Backend.CheckedResult = false;
        factory.Backend.TextResults.Enqueue("draft");
        factory.Backend.ValueResult = "old";
        factory.Backend.CurrentAddress = "https://example.test/login";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web negated assertions", TestMethods.Placeholder);
        var form = context.Web().Page<LoginPage>().Form;

        await form.Status.ShouldNot.BeVisibleAsync(TimeSpan.FromSeconds(1));
        await form.Submit.ShouldNot.BeEnabledAsync(TimeSpan.FromSeconds(1));
        await form.RememberMe.ShouldNot.BeCheckedAsync(TimeSpan.FromSeconds(1));
        await form.Status.ShouldNot.HaveTextAsync("ready", TimeSpan.FromSeconds(1));
        await form.Password.ShouldNot.HaveValueAsync("new", TimeSpan.FromSeconds(1));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var assertions = host.Trace.Snapshot().Tests.Single().Entries
            .Where(item => item.Kind == "assert.web")
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(assertions, Has.Length.EqualTo(5), "one assertion entry per negated assertion");
            Assert.That(assertions.Select(item => item.Attributes["web.assert.negated"]), Is.All.EqualTo("true"));
            Assert.That(assertions.Select(item => item.Attributes["web.expectation"]), Is.EqualTo(new[]
            {
                "not be visible",
                "not be enabled",
                "not be checked",
                "not have text \"ready\"",
                "not have the expected value"
            }));
            Assert.That(context.RecordedObservations.Count(item => item.Kind == "web.page.verified"),
                Is.EqualTo(5), "a passing negated assertion still verifies the page");
        });
    }

    [Test]
    public async Task NegatedAssertion_ShouldFailWhenTheCheckKeepsHolding()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web negated timeout", TestMethods.Placeholder);
        var form = context.Web().Page<LoginPage>().Form;

        var exception = Assert.ThrowsAsync<WebAssertionException>(async () =>
            await form.Status.ShouldNot.BeVisibleAsync(TimeSpan.FromMilliseconds(150)));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        var assertion = host.Trace.Snapshot().Tests.Single().Entries.Single(item => item.Kind == "assert.web");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("should not be visible"));
            Assert.That(exception.Message, Does.Contain("within 00:00:00.1500000"),
                "the negated assertion honours its timeout");
            Assert.That(exception.Message, Does.Contain("Last observed: visible"));
            Assert.That(assertion.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(assertion.Attributes["web.assert.negated"], Is.EqualTo("true"));
            Assert.That(assertion.Attributes["web.expectation"], Is.EqualTo("not be visible"));
        });
    }

    [Test]
    public async Task NegatedTextAssertion_ShouldFailWhenTheTextMatches()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextDefault = "ready";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web negated text", TestMethods.Placeholder);
        var form = context.Web().Page<LoginPage>().Form;

        var exception = Assert.ThrowsAsync<WebAssertionException>(async () =>
            await form.Status.ShouldNot.HaveTextAsync("ready", TimeSpan.FromMilliseconds(150)));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("should not have text \"ready\""));
            Assert.That(exception.Message, Does.Contain("text was \"ready\""));
        });
    }

    [Test]
    public async Task Navigate_ShouldRecordTheVisitedPagePathAfterRedirects()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.NavigateAddressOverride = "https://example.test/dashboard?tab=orders#top";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web page visited", TestMethods.Placeholder);

        await context.Web().Page<LoginPage>().OpenAsync("https://example.test/login?token=secret#form");

        var visited = context.RecordedObservations.Where(item => item.Kind == "web.page.visited").ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(visited.Select(item => item.Identifier), Is.EqualTo(new[] { "/dashboard" }),
                "the final address wins and only the path is recorded");
            Assert.That(visited.Single().TargetName, Is.EqualTo("Web"));
            Assert.That(visited.Single().Metadata!["web.session"], Is.EqualTo("Default"));
        });
    }

    [Test]
    public async Task Assertion_ShouldRecordTheVerifiedPagePath()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextResults.Enqueue("ready");
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web page verified", TestMethods.Placeholder);
        var page = context.Web().Page<LoginPage>();

        await page.OpenAsync("https://example.test/login");
        await page.Form.Status.Should.HaveTextAsync("ready", TimeSpan.FromSeconds(1));

        var verified = context.RecordedObservations.Where(item => item.Kind == "web.page.verified").ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(verified.Select(item => item.Identifier), Is.EqualTo(new[] { "/login" }));
            Assert.That(context.RecordedObservations.Any(item => item.Kind == "web.page.visited"), Is.True);
        });
    }

}
