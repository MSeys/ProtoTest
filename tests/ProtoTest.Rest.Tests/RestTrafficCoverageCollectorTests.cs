namespace ProtoTest.Rest.Tests;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

[TestFixture]
public sealed class RestTrafficCoverageCollectorTests
{
    [Test]
    public void GetReportItems_ShouldReportFieldsNoShapeMentioned()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response(
            "GET",
            "/orders/{id}",
            200,
            """{"id":42,"name":"Ada","customer":{"id":7,"email":"ada@example.test"}}"""));
        collector.Collect(Shape("GET", "/orders/{id}", 200, "$", "$.id", "$.customer", "$.customer.id"));

        var item = collector.GetReportItems().Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.TargetName, Is.EqualTo("Orders"));
            Assert.That(item.Category, Is.EqualTo("REST traffic"));
            Assert.That(item.Identifier, Is.EqualTo("GET /orders/{id} · 200"));
            Assert.That(item.Kind, Is.EqualTo(ProtoReportItemKinds.Traffic));
            Assert.That(item.IsCovered, Is.Null, "the route row is an aggregate, not a coverage unit");
            Assert.That(item.Children!.Select(child => child.Identifier),
                Is.EqualTo(new[] { "$.name", "$.customer.email" }));
            Assert.That(item.Children!.Select(child => child.IsCovered), Is.All.False);
        });
    }

    [Test]
    public void GetReportItems_ShouldReportEveryFieldOfAnUnassertedResponse()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response("GET", "/orders", 200, """{"id":42,"customer":{"email":"ada@example.test"}}"""));

        var item = collector.GetReportItems().Single();

        Assert.That(item.Children!.Select(child => child.Identifier), Is.EqualTo(new[] { "$.id", "$.customer" }));
    }

    [Test]
    public void GetReportItems_ShouldCountAFieldAssertedByAnyShapeAsAsserted()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response("GET", "/orders/{id}", 200, """{"id":42,"name":"Ada","extra":true}"""));
        collector.Collect(Shape("GET", "/orders/{id}", 200, "$", "$.id"));
        collector.Collect(Shape("GET", "/orders/{id}", 200, "$", "$.name"));

        var item = collector.GetReportItems().Single();

        Assert.That(item.Children!.Select(child => child.Identifier), Is.EqualTo(new[] { "$.extra" }),
            "a field any shape mentioned is asserted for the run");
    }

    [Test]
    public void GetReportItems_ShouldUnionEveryObservedBodyForTheRoute()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response("GET", "/orders/{id}", 200, """{"id":42,"name":"Ada"}"""));
        collector.Collect(Response("GET", "/orders/{id}", 200, """{"id":43,"status":"active"}"""));
        collector.Collect(Shape("GET", "/orders/{id}", 200, "$", "$.id"));

        var item = collector.GetReportItems().Single();

        Assert.That(item.Children!.Select(child => child.Identifier),
            Is.EqualTo(new[] { "$.name", "$.status" }));
    }

    [Test]
    public void GetReportItems_ShouldKeepRoutesAndStatusesApart()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response("GET", "/orders/{id}", 200, """{"id":42,"name":"Ada"}"""));
        collector.Collect(Response("GET", "/orders/{id}", 404, """{"error":"not found"}"""));
        collector.Collect(Response("GET", "/orders", 200, """{"total":1}"""));
        collector.Collect(Shape("GET", "/orders/{id}", 200, "$", "$.id"));

        var items = collector.GetReportItems().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => item.Identifier), Is.EqualTo(new[]
            {
                "GET /orders · 200",
                "GET /orders/{id} · 200",
                "GET /orders/{id} · 404"
            }));
            Assert.That(items.SelectMany(item => item.Children!).Select(child => child.Identifier),
                Is.EqualTo(new[] { "$.total", "$.name", "$.error" }));
        });
    }

    [Test]
    public void GetReportItems_ShouldReturnNothingWhenEveryFieldWasMentioned()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response("GET", "/orders/{id}", 200, """{"id":42}"""));
        collector.Collect(Shape("GET", "/orders/{id}", 200, "$", "$.id"));

        Assert.That(collector.GetReportItems(), Is.Empty);
    }

    [Test]
    public void GetReportItems_ShouldNeverCountObservedFieldsAsCovered()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response("GET", "/orders", 200, """{"id":42}"""));

        var totals = collector.GetReportItems().CoverageTotals();

        Assert.Multiple(() =>
        {
            Assert.That(totals.Total, Is.Zero, "the assertion-level coverage rule stays: observed is never covered");
            Assert.That(totals.Covered, Is.Zero);
        });
    }

    [Test]
    public void GetReportItems_ShouldIgnoreShapeObservationsWithoutStructuredRouting()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(new ProtoObservation(
            "Orders",
            "http.contract.shape",
            "GET /orders",
            new RestShapeMatchData("GET /orders", ["$.id"], typeof(object), StatusCode: 200)));
        collector.Collect(Response("GET", "/orders", 200, """{"id":42}"""));

        Assert.That(collector.GetReportItems().Single().Children!.Single().Identifier, Is.EqualTo("$.id"),
            "a shape observation without method and route cannot claim the field was asserted");
    }

    [Test]
    public void GetReportItems_ShouldIgnoreNonJsonAndTruncatedBodies()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        collector.Collect(Response("GET", "/orders", 200, "not json"));
        collector.Collect(Response("GET", "/orders", 200, """{"id":42}""" + Environment.NewLine + "… [4 characters truncated]"));
        collector.Collect(Response("GET", "/orders", 200, """{"id":42}"""));

        Assert.That(collector.GetReportItems().Single().Children!.Single().Identifier, Is.EqualTo("$.id"));
    }

    [Test]
    public void CanCollect_ShouldIgnoreOtherTargets()
    {
        var collector = new RestTrafficCoverageCollector("Orders");
        var observation = new ProtoObservation(
            "Other",
            "http.response",
            "GET /orders",
            new RestResponseData("GET", "/orders", 200, """{"id":42}""", new Dictionary<string, string>()));

        Assert.That(collector.CanCollect(observation), Is.False);
    }

    [Test]
    public async Task Collector_ShouldReportFromTheRealObservationFlow()
    {
        var services = new ServiceCollection();
        await using var context = new ProtoExecutionContext(
            "TestContext",
            services.BuildServiceProvider().CreateScope(),
            "00003",
            TestMethods.Placeholder);
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":42,"name":"Ada"}""")
            }
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.prototest.dev") };
        var builder = new RestRequestBuilder(client, context, "Orders");

        using var response = await builder.GetAsync("/orders/{id}", new { id = 42 });
        response.Should.MatchShape(new { id = 42 });

        var collector = new RestTrafficCoverageCollector("Orders");
        foreach (var observation in context.RecordedObservations.Where(collector.CanCollect))
        {
            collector.Collect(observation);
        }

        var item = collector.GetReportItems().Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Identifier, Is.EqualTo("GET /orders/{id} · 200"));
            Assert.That(item.Children!.Single().Identifier, Is.EqualTo("$.name"));
        });
        handler.Dispose();
    }

    [Test]
    public async Task AddCollector_ShouldRegisterTheTrafficCollectorOnTheTarget()
    {
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest
            .AddClient("Orders", "https://orders.test")
            .AddCollector<RestTrafficCoverageCollector>());

        await using var host = builder.Build();

        Assert.That(host, Is.Not.Null);
    }

    private static ProtoObservation Response(string method, string route, int status, string body)
        => new(
            "Orders",
            "http.response",
            $"{method} {route}",
            new RestResponseData(method, route, status, body, new Dictionary<string, string>()));

    private static ProtoObservation Shape(string method, string route, int status, params string[] matched)
        => new(
            "Orders",
            "http.contract.shape",
            $"{method} {route}",
            new RestShapeMatchData($"{method} {route}", matched, typeof(object), status)
            {
                Method = method,
                RouteTemplate = route
            });
}
