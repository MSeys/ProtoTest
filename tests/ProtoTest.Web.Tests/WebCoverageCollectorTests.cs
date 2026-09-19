namespace ProtoTest.Web.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

[TestFixture]
public sealed class WebCoverageCollectorTests
{
    [Test]
    public void Collector_ShouldCountVerificationsAndExposeTheInventory()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Pages:0"] = "/inventory",
                ["ProtoTest:Web:Pages:1"] = "dashboard/"
            })
            .Build();
        var collector = new WebCoverageCollector("Web", configuration);

        collector.Collect(new ProtoObservation("Web", "web.page.visited", "/login"));
        collector.Collect(new ProtoObservation("Web", "web.page.verified", "/login"));
        collector.Collect(new ProtoObservation("Web", "web.page.verified", "/login"));
        collector.Collect(new ProtoObservation("Web", "web.page.available", "/new"));

        var items = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(items.Keys, Is.EquivalentTo(new[] { "/login", "/new", "/inventory", "/dashboard" }));
            Assert.That(items["/login"].IsCovered, Is.True);
            Assert.That(items["/login"].Count, Is.EqualTo(2), "count is the number of verifications");
            Assert.That(items["/login"].DisplayName, Is.EqualTo("/login"));
            Assert.That(items["/login"].Status, Is.EqualTo(ProtoReportStatus.Success));
            Assert.That(items["/new"].IsCovered, Is.False);
            Assert.That(items["/new"].Count, Is.EqualTo(0));
            Assert.That(items["/inventory"].IsCovered, Is.False);
            Assert.That(items["/dashboard"].IsCovered, Is.False, "the configured path is normalized to one identity");
            Assert.That(items.Values.All(item =>
                item.Category == "Web" && item.Kind == ProtoReportItemKinds.Coverage), Is.True);
        });
    }

    [Test]
    public async Task Collector_ShouldBeSafeUnderConcurrentCollectAndReport()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Pages:0"] = "/users/{id}",
                ["ProtoTest:Web:Pages:1"] = "/static"
            })
            .Build();
        var collector = new WebCoverageCollector("Web", configuration);
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try
            {
                for (var index = 0; index < 200; index++)
                {
                    collector.Collect(new ProtoObservation(
                        "Web",
                        "web.page.verified",
                        index % 2 == 0 ? "/users/42" : "/static"));
                    if (index % 20 == 0) _ = collector.GetReportItems().Count();
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        })).ToArray();
        await Task.WhenAll(workers);

        var items = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, "collect and report never raced on the item store");
            Assert.That(items["/users/{id}"].Count, Is.EqualTo(800),
                "every concurrent verification of the concrete path lands on the pattern exactly once");
            Assert.That(items["/static"].Count, Is.EqualTo(800));
        });
    }

    [Test]
    public void Collector_ShouldIgnoreOtherTargetsAndKinds()
    {
        var collector = new WebCoverageCollector("Web");

        Assert.Multiple(() =>
        {
            Assert.That(collector.CanCollect(new ProtoObservation("Web", "web.page.visited", "/a")), Is.True);
            Assert.That(collector.CanCollect(new ProtoObservation("Web", "web.page.verified", "/a")), Is.True);
            Assert.That(collector.CanCollect(new ProtoObservation("Web", "web.page.available", "/a")), Is.True);
            Assert.That(collector.CanCollect(new ProtoObservation("Web", "web.navigate", "/a")), Is.False);
            Assert.That(collector.CanCollect(new ProtoObservation("Api", "web.page.visited", "/a")), Is.False);
            Assert.That(collector.CanCollect(new ProtoObservation("Web", "sheets.range", "Sheet!A1")), Is.False);
        });
    }

    [Test]
    public void Collector_ShouldCoverDynamicInventoryPatternFromConcreteVerification()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Pages:0"] = "/users/{id}",
                ["ProtoTest:Web:Pages:1"] = "/docs/{...}"
            })
            .Build();
        var collector = new WebCoverageCollector("Web", configuration);

        collector.Collect(new ProtoObservation("Web", "web.page.visited", "/users/42"));
        collector.Collect(new ProtoObservation("Web", "web.page.verified", "/users/42"));
        collector.Collect(new ProtoObservation("Web", "web.page.verified", "/users/7"));

        var items = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(items.Keys, Is.EquivalentTo(new[] { "/users/{id}", "/docs/{...}" }),
                "the concrete paths land on the pattern instead of creating their own items");
            Assert.That(items["/users/{id}"].IsCovered, Is.True);
            Assert.That(items["/users/{id}"].Count, Is.EqualTo(2), "the pattern counts one per verification");
            Assert.That(items["/docs/{...}"].IsCovered, Is.False);
        });
    }

    [Test]
    public void Collector_ShouldPreferAnExactEntryAndANonCatchAllPattern()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // The catch-all and the pattern both match; the exact entry and the specific pattern win.
                ["ProtoTest:Web:Pages:0"] = "/users/{id}",
                ["ProtoTest:Web:Pages:1"] = "/users/me",
                ["ProtoTest:Web:Pages:2"] = "/docs/{...}",
                ["ProtoTest:Web:Pages:3"] = "/docs/{section}"
            })
            .Build();
        var collector = new WebCoverageCollector("Web", configuration);

        collector.Collect(new ProtoObservation("Web", "web.page.verified", "/users/me"));
        collector.Collect(new ProtoObservation("Web", "web.page.verified", "/docs/intro"));

        var items = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(items["/users/me"].IsCovered, Is.True,
                "an exact inventory entry wins over a pattern that also matches");
            Assert.That(items["/users/me"].Count, Is.EqualTo(1));
            Assert.That(items["/users/{id}"].IsCovered, Is.False);
            Assert.That(items["/docs/{section}"].IsCovered, Is.True,
                "a specific pattern wins over the catch-all that also matches");
            Assert.That(items["/docs/{...}"].IsCovered, Is.False);
        });
    }

    [Test]
    public void Collector_ShouldKeepUnmatchedConcretePathsAsTheirOwnItem()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Pages:0"] = "/users/{id}"
            })
            .Build();
        var collector = new WebCoverageCollector("Web", configuration);

        collector.Collect(new ProtoObservation("Web", "web.page.verified", "/login"));

        var items = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(items.Keys, Is.EquivalentTo(new[] { "/users/{id}", "/login" }));
            Assert.That(items["/login"].IsCovered, Is.True);
            Assert.That(items["/login"].Count, Is.EqualTo(1));
            Assert.That(items["/users/{id}"].IsCovered, Is.False);
        });
    }

    [Test]
    public void Collector_ShouldMergeSourceFolderInventoryAndIgnoreInventorySettings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prototest-web-collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "pages"));
        File.WriteAllText(Path.Combine(folder, "pages", "index.tsx"), string.Empty);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Pages:0"] = "/explicit",
                    ["ProtoTest:Web:Pages:Source"] = folder,
                    ["ProtoTest:Web:Pages:Framework"] = "next"
                })
                .Build();
            var collector = new WebCoverageCollector("Web", configuration);

            var items = collector.GetReportItems().ToDictionary(item => item.Identifier, StringComparer.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(items.Keys, Is.EquivalentTo(new[] { "/explicit", "/" }));
                Assert.That(items["/"].IsCovered, Is.False, "discovered pages start uncovered");
                Assert.That(items.ContainsKey(folder), Is.False, "Source is a setting, not a page");
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void VueRouteDiscovery_ShouldNormalizeDynamicSegments()
    {
        var parsed = VueRouteDiscovery.Parse("""["/users/:id","/orders/:id?","/legacy/*"]""").ToArray();

        Assert.That(parsed, Is.EqualTo(new[] { "/users/{id}", "/orders/{id}", "/legacy/{...}" }));
    }

    [Test]
    public void VueRouteDiscovery_ShouldKeepTheRootRoute()
    {
        var parsed = VueRouteDiscovery.Parse("""["/","/users/:id"]""").ToArray();

        Assert.That(parsed, Is.EqualTo(new[] { "/", "/users/{id}" }),
            "the root route is a page like any other, not an empty route definition");
    }

    [Test]
    public void VueRouteDiscovery_ShouldMapRegexAndTrailingSplatsAndIgnoreRelativePaths()
    {
        var parsed = VueRouteDiscovery.Parse(
            """["/docs/:pathMatch(.*)*","/files/:rest*","/items/:id(\\d+)","child",""]""").ToArray();

        Assert.That(parsed, Is.EqualTo(new[] { "/docs/{...}", "/files/{...}", "/items/{id}" }),
            "regex catch-alls and trailing splats become the rest pattern; relative paths are not routes");
    }

    [Test]
    public void Matches_ShouldMatchInventoryPatternsSegmentWise()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebPagePath.Matches("/users/{id}", "/users/42"), Is.True);
            Assert.That(WebPagePath.Matches("/users/{id}", "/users/42/edit"), Is.False);
            Assert.That(WebPagePath.Matches("/users/{id}", "/users"), Is.False);
            Assert.That(WebPagePath.Matches("/docs/{...}", "/docs/a/b"), Is.True);
            Assert.That(WebPagePath.Matches("/docs/{...}", "/docs"), Is.True);
            Assert.That(WebPagePath.Matches("/", "/"), Is.True);
            Assert.That(WebPagePath.Matches("/users/static", "/users/STATIC"), Is.True, "matching is case-insensitive");
        });
    }
}
