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
    public async Task Flow_ShouldRunStepsInOrderInsideOneTracedOperation()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web flow", TestMethods.Placeholder);
        var form = context.Web().Page<LoginPage>().Form;
        var customStepRan = false;

        await form.Flow("Sign in")
            .Fill(f => f.Password, "super-secret")
            .Check(f => f.RememberMe)
            .Uncheck(f => f.RememberMe)
            .Select(f => f.Language, "nl")
            .Press(f => f.Password, WebKey.Enter)
            .Do((f, _) =>
            {
                customStepRan = ReferenceEquals(f, form);
                return ValueTask.CompletedTask;
            })
            .Click(f => f.Submit)
            .RunAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var flow = entries.Single(entry => entry.Kind == "web.flow");
        var steps = entries
            .Where(entry => entry.ParentId == flow.Id && entry.Kind != "web.session.initialize")
            .Select(entry => entry.Kind)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.Operations.Select(item => item.Kind), Is.EqualTo(
                new[] { "fill", "check:True", "check:False", "select", "press", "click" }));
            Assert.That(customStepRan, Is.True);
            Assert.That(flow.Name, Is.EqualTo("WEB flow · Sign in"));
            Assert.That(flow.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(flow.Attributes["web.flow.step_count"], Is.EqualTo("7"));
            Assert.That(steps, Is.EqualTo(new[]
            {
                "web.fill", "web.check", "web.check", "web.select_option", "web.press", "web.click"
            }));
        });
    }

    [Test]
    public async Task Flow_ShouldStopAtTheFailingStepAndFailTheFlowOperation()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web flow", TestMethods.Placeholder);
        var expected = new InvalidOperationException("submit is broken");

        var actual = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Web().Page<LoginPage>().Form.Flow("Sign in")
                .Do((_, _) =>
                {
                    factory.Backend.Failure = expected;
                    return ValueTask.CompletedTask;
                })
                .Click(f => f.Submit)
                .Fill(f => f.Password, "never typed")
                .RunAsync());
        await host.CompleteTestAsync(ProtoTestResult.Failed(expected));

        var flow = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "web.flow");
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(factory.Backend.Operations.Select(item => item.Kind), Is.EqualTo(new[] { "click" }));
            Assert.That(flow.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(flow.Error?.Message, Is.EqualTo("submit is broken"));
        });
    }

    [Test]
    public async Task Flow_ShouldOnlyRunOnce()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web flow", TestMethods.Placeholder);
        var flow = context.Web().Page<LoginPage>().Form.Flow("Submit").Click(f => f.Submit);

        await flow.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await flow.RunAsync());
            Assert.Throws<InvalidOperationException>(() => flow.Click(f => f.Submit));
            Assert.That(factory.Backend.Operations.Count(item => item.Kind == "click"), Is.EqualTo(1));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task InteractAsync_ShouldWrapAnArbitraryInteractionInANamedFlow()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web interaction", TestMethods.Placeholder);

        await context.Web().Page<LoginPage>().Form.InteractAsync(
            "Submit twice",
            async form =>
            {
                await form.Submit.ClickAsync();
                await form.Submit.ClickAsync();
            });
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var flow = entries.Single(entry => entry.Kind == "web.flow");
        Assert.Multiple(() =>
        {
            Assert.That(flow.Name, Is.EqualTo("WEB flow · Submit twice"));
            Assert.That(flow.Attributes["web.flow.step_count"], Is.EqualTo("1"));
            Assert.That(entries.Count(entry => entry.Kind == "web.click" && entry.ParentId == flow.Id), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task JQueryIdleWait_ShouldPollUntilJQueryReportsNoActiveRequests()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.EvaluateResults.Enqueue(false);
        factory.Backend.EvaluateResults.Enqueue(false);
        factory.Backend.EvaluateResults.Enqueue(true);
        var host = CreateHost(factory, builder => builder.AddWebWait<JQueryIdleWait>(
            WebWaitTiming.After,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1),
            WebOperationKind.Click));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("jquery wait", TestMethods.Placeholder);

        await context.Web().Page<LoginPage>().Form.Submit.ClickAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var wait = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "web.wait");
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.EvaluatedScripts, Has.Count.EqualTo(3));
            Assert.That(factory.Backend.EvaluatedScripts, Has.All.Contains("window.jQuery.active === 0"));
            Assert.That(wait.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(wait.Attributes["web.wait.last_observed"], Is.EqualTo("jQuery is absent or has no active requests"));
        });
    }

    [Test]
    public async Task JQueryIdleWait_ShouldTimeOutWithTheLastObservation()
    {
        var factory = new FakeBackendFactory();
        // The wait polls until its timeout, and how often that is depends on the machine: a queue of
        // answers would run dry and read as ready. jQuery stays busy for as long as this test runs.
        factory.Backend.EvaluateDefault = false;
        var host = CreateHost(factory, builder => builder.AddWebWait<JQueryIdleWait>(
            WebWaitTiming.Before,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(5),
            WebOperationKind.Click));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("jquery wait", TestMethods.Placeholder);

        var exception = Assert.ThrowsAsync<WebWaitTimeoutException>(async () =>
            await context.Web().Page<LoginPage>().Form.Submit.ClickAsync());
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("jQuery.active is greater than zero"));
            Assert.That(factory.Backend.Operations.Where(item => item.Kind == "click"), Is.Empty);
        });
    }

}
