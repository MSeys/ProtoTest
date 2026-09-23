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
    public async Task WebCoverage_ShouldReportVisitedVerifiedAndInventoriedPages()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextResults.Enqueue("ready");
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Pages:0"] = "/inventory",
                ["ProtoTest:Web:Pages:1"] = "dashboard"
            })));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web coverage", TestMethods.Placeholder);
        var session = context.Web();

        await session.Page<LoginPage>().OpenAsync("https://example.test/login");
        factory.Backend.CurrentAddress = "https://example.test/dashboard";
        await session.Page<LoginPage>().Form.Status.Should.HaveTextAsync("ready", TimeSpan.FromSeconds(1));
        context.RecordObservation("Web", "web.page.available", "/new-page");

        var items = context.Services.GetServices<IProtoCollector>()
            .OfType<WebCoverageCollector>()
            .Single()
            .GetReportItems()
            .ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(items.Keys, Is.EquivalentTo(new[] { "/login", "/dashboard", "/new-page", "/inventory" }));
            Assert.That(items["/dashboard"].IsCovered, Is.True, "a verified page is covered");
            Assert.That(items["/dashboard"].Count, Is.EqualTo(1), "count is the number of verifications");
            Assert.That(items["/dashboard"].DisplayName, Is.EqualTo("/dashboard"));
            Assert.That(items["/login"].IsCovered, Is.False, "a visited-only page is uncovered");
            Assert.That(items["/new-page"].IsCovered, Is.False, "a discovered-only page is uncovered");
            Assert.That(items["/inventory"].IsCovered, Is.False, "an inventoried-only page is uncovered");
            Assert.That(items.Values.All(item =>
                item.Category == "Web" && item.Kind == ProtoReportItemKinds.Coverage), Is.True);
        });
    }

    [Test]
    public async Task RouteDiscovery_ShouldRecordVueRoutesWhenTheRouterAnswers()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.JsonResult = """["/orders","/orders/new"]""";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue discovery", TestMethods.Placeholder);

        await context.Web(discoverRoutes: true).Page<LoginPage>().OpenAsync("https://example.test/orders");

        var available = context.RecordedObservations
            .Where(item => item.Kind == "web.page.available")
            .Select(item => item.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(available, Is.EquivalentTo(new[] { "/orders", "/orders/new" }));
            Assert.That(factory.Backend.EvaluatedScripts, Has.Some.Contains("__vue_app__"));
        });
    }

    [Test]
    public async Task RouteDiscovery_ShouldBeANoOpWithoutAVueRouter()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue absent", TestMethods.Placeholder);

        await context.Web(discoverRoutes: true).Page<LoginPage>().OpenAsync("https://example.test/login");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(context.RecordedObservations.Any(item => item.Kind == "web.page.available"), Is.False);
            Assert.That(context.RecordedObservations.Any(item => item.Kind == "web.page.visited"), Is.True);
            Assert.That(factory.Backend.EvaluatedScripts, Has.Some.Contains("__vue_app__"));
        });
    }

    [Test]
    public async Task MiddlewareAndNamedWaits_ShouldWrapSelectedOperations()
    {
        var factory = new FakeBackendFactory();
        var middlewareProbe = new MiddlewareProbe();
        var waitProbe = new WaitProbe();
        var host = CreateHost(factory, builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(middlewareProbe);
                services.AddSingleton(waitProbe);
            });
            builder.AddWebMiddleware<RecordingMiddleware>();
            builder.AddWebWait<TwoPassWait>(
                WebWaitTiming.Before,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(1),
                WebOperationKind.Click);
        });
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);

        await context.Web().Page<LoginPage>().Form.Submit.ClickAsync();

        Assert.Multiple(() =>
        {
            Assert.That(middlewareProbe.Events, Is.EqualTo(new[] { "before:Click", "after:Click" }));
            Assert.That(waitProbe.Observations, Is.EqualTo(2));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries, Has.Some.Matches<ProtoTraceEntry>(
                entry => entry.Kind == "web.wait" && entry.Outcome == ProtoTraceOutcome.Succeeded));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Operations_ShouldTraceSemanticsAndRedactFillValues()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        await context.Web().Page<LoginPage>().Form.Password.FillAsync("super-secret");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var trace = host.Trace.Snapshot().Tests.Single();
        var fill = trace.Entries.Single(entry => entry.Kind == "web.fill");
        var backend = trace.Entries.Single(entry => entry.Kind == "web.backend.execute");
        Assert.Multiple(() =>
        {
            Assert.That(fill.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(fill.Attributes["web.component"], Is.EqualTo("LoginPage.Form"));
            Assert.That(fill.Attributes["web.locator"], Is.EqualTo("Label(\"Password\", exact: true)"));
            Assert.That(fill.Attributes["web.value"], Is.EqualTo("[REDACTED]"));
            Assert.That(fill.Attributes.Values, Has.None.Contains("super-secret"));
            Assert.That(backend.ParentId, Is.EqualTo(fill.Id));
        });
    }

    [Test]
    public async Task FailureCapture_ShouldPreserveOriginalExceptionAndRegisterArtifacts()
    {
        var expected = new InvalidOperationException("native click failed");
        var factory = new FakeBackendFactory { Failure = expected };
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);

        var actual = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Web().Page<LoginPage>().Form.Submit.ClickAsync());
        Assert.That(actual, Is.SameAs(expected));
        Assert.That(context.Attachments.Select(item => item.Name), Has.Some.EndsWith("web-failure.txt"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(expected));
        var click = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "web.click");
        Assert.That(click.Error?.Message, Is.EqualTo("native click failed"));
    }

    [Test]
    public async Task DownloadAsync_ShouldUseTheCapabilityTraceAndAttachTheFile()
    {
        var stub = new DownloadingBackend
        {
            DownloadResult = new WebDownload("orders.csv", "text/csv", Encoding.UTF8.GetBytes("id,total\n1,42"))
        };
        var factory = new FakeBackendFactory(stub);
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web download", TestMethods.Placeholder);
        var triggered = false;
        var timeout = TimeSpan.FromSeconds(3);

        var download = await context.Web().DownloadAsync(
            _ =>
            {
                triggered = true;
                return Task.CompletedTask;
            },
            name: "Monthly orders.csv",
            timeout: timeout);

        var entry = host.Trace.Snapshot().Tests.Single().Entries.Single(item => item.Kind == "web.download");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Is.True);
            Assert.That(stub.TriggerCount, Is.EqualTo(1));
            Assert.That(stub.LastTimeout, Is.EqualTo(timeout));
            Assert.That(download.FileName, Is.EqualTo("Monthly orders.csv"));
            Assert.That(download.MediaType, Is.EqualTo("text/csv"), "an explicit name with the same extension keeps the type");
            Assert.That(download.Size, Is.EqualTo(13));
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(entry.Attributes["web.download.name"], Is.EqualTo("Monthly orders.csv"));
            Assert.That(entry.Attributes["web.download.media_type"], Is.EqualTo("text/csv"));
            Assert.That(entry.Attributes["web.download.size"], Is.EqualTo("13"));
            Assert.That(entry.Attributes["web.download.requested_name"], Is.EqualTo("Monthly orders.csv"));
            Assert.That(entry.Attributes["web.download.timeout"], Is.EqualTo(timeout.ToString()));
            Assert.That(context.Attachments.Select(item => item.Name),
                Has.Some.EndsWith("web-default-download-1-monthly-orders.csv"));
        });
    }

    [Test]
    public async Task DownloadAsync_ShouldFailCleanlyWhenTheBackendHasNoDownloadCapability()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web download unsupported", TestMethods.Placeholder);
        var triggered = false;

        var exception = Assert.ThrowsAsync<WebBackendCapabilityException>(async () =>
            await context.Web().DownloadAsync(_ =>
            {
                triggered = true;
                return Task.CompletedTask;
            }));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Is.False, "the trigger never runs when the backend cannot capture");
            Assert.That(exception!.Message, Does.Contain("Fake").And.Contains("IWebBackendDownloads"));
        });
    }

    [Test]
    public async Task SeleniumDownload_ShouldFailWithTheDocumentedCapabilityException()
    {
        var host = new ProtoHostBuilder().AddWeb(() => new StubWebDriver()).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium download", TestMethods.Placeholder);
        var triggered = false;

        var exception = Assert.ThrowsAsync<WebBackendCapabilityException>(async () =>
            await context.Web().DownloadAsync(_ =>
            {
                triggered = true;
                return Task.CompletedTask;
            }));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Is.False, "the trigger never runs on Selenium");
            Assert.That(exception!.Message, Does.Contain("Selenium").And.Contains("Playwright"));
        });
    }

    [Test]
    public async Task Backend_ShouldBeOwnedByCoreClientLifecycle()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(factory.Backend.Disposed, Is.True);
    }

    [Test]
    public async Task BackendArtifacts_ShouldFinalizeBeforeCorePublishesAttachments()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.AddAttachmentOnComplete = true;
        var publisher = new RecordingAttachmentPublisher();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder, attachmentPublisher: publisher);
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(publisher.Attachments.Select(item => item.Name), Has.Some.EndsWith("native-trace.zip"));
    }

    [Test]
    public async Task SeleniumDiagnostics_ShouldBeRegisteredAsCoreAttachmentBeforePublishing()
    {
        var driver = new StubWebDriver();
        var publisher = new RecordingAttachmentPublisher();
        var host = new ProtoHostBuilder()
            .AddWeb(
                () => driver,
                options => options.DiagnosticTraceRetention = SeleniumDiagnosticTraceRetention.Always)
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder, attachmentPublisher: publisher);
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(context.Attachments.Select(item => item.Name), Has.Some.EndsWith("selenium-default-diagnostics.json"));
            Assert.That(publisher.Attachments.Select(item => item.Name), Has.Some.EndsWith("selenium-default-diagnostics.json"));
            Assert.That(driver.QuitCalled, Is.True);
        });
    }

    [Test]
    public async Task SeleniumDiagnostics_ShouldReportAFinalizationFailureWithoutFailingTeardown()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder()
            .AddWeb(
                () => driver,
                options => options.DiagnosticTraceRetention = SeleniumDiagnosticTraceRetention.Always)
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        await context.Web().GetBackendAsync<ProtoTest.Web.Selenium.SeleniumWebBackend>();
        driver.ThrowOnUrl = true;

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(context.Attachments.Select(item => item.Name),
                Has.None.EndsWith("selenium-default-diagnostics.json"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "web.diagnostics.artifact_failed" &&
                    entry.Outcome == ProtoTraceOutcome.Failed));
        });
    }

}
