namespace ProtoTest.OpenApi.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.OpenApi;

[TestFixture]
public class OpenApiCoverageCollectorTests
{
    private class DummyPayload { }

    [Test]
    public void GetReportItems_ShouldIncludeFullBaseline_WhenNoHitsRecorded()
    {
        // Arrange
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);

        // Act
        var items = collector.GetReportItems().ToList();

        // Assert
        Assert.That(items, Has.Count.EqualTo(1));

        var root = items.Single();
        Assert.That(root.Identifier, Is.EqualTo("GET /users/{id}"));
        Assert.That(root.IsVisited, Is.False);
        Assert.That(root.HitCount, Is.Zero);

        var children = (List<CoverageItem>)root.Metadata!["Children"];
        Assert.That(children.Any(c => c.Identifier == "GET /users/{id} -> 200" && !c.IsVisited), Is.True);
        Assert.That(children.Any(c => c.Identifier == "GET /users/{id} -> 404" && !c.IsVisited), Is.True);
        Assert.That(children.Any(c => c.Identifier == "GET /users/{id} -> $.id" && !c.IsVisited), Is.True);
    }

    [Test]
    public void RecordHit_ShouldIncrementHitCount_ForEndpointStatusAndShapeProperties()
    {
        // Arrange
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);
        var emptyHeaders = new Dictionary<string, string>();

        // Act - 1. Record HTTP Hits (verstuurd via RestRequestBuilder)
        collector.RecordHit(new CoverageHit(
            TargetName: "TestApi",
            Identifier: "GET /users/{id}",
            Data: new RestHitData("GET", "/users/{id}", 200, "{}", emptyHeaders)
        ));

        collector.RecordHit(new CoverageHit(
            TargetName: "TestApi",
            Identifier: "GET /users/{id}",
            Data: new RestHitData("GET", "/users/{id}", 200, "{}", emptyHeaders)
        ));

        // Act - 2. Record ShapeMatch Hit (verstuurd via RestResponse.ShouldMatchShape)
        // RouteTemplate is de volledige routeIdentifier ("GET /users/{id}")
        collector.RecordHit(new CoverageHit(
            TargetName: "TestApi",
            Identifier: "GET /users/{id}",
            Data: new ShapeMatchData(
                RouteTemplate: "GET /users/{id}",
                MatchedProperties: new[] { "$.id", "$.name" },
                TargetType: typeof(DummyPayload)
            )
        ));

        var items = collector.GetReportItems().ToList();

        // Assert - Root Item Checks
        var root = items.Single(i => i.Identifier == "GET /users/{id}");
        Assert.That(root.IsVisited, Is.True);
        Assert.That(root.HitCount, Is.EqualTo(2));

        var children = (List<CoverageItem>)root.Metadata!["Children"];

        // Assert - Status Code Child
        var status200 = children.Single(c => c.Identifier == "GET /users/{id} -> 200");
        Assert.That(status200.IsVisited, Is.True);
        Assert.That(status200.HitCount, Is.EqualTo(2));

        // Assert - Matched Shape Property Child
        var propId = children.Single(c => c.Identifier == "GET /users/{id} -> $.id");
        Assert.That(propId.IsVisited, Is.True);
        Assert.That(propId.HitCount, Is.EqualTo(1));

        // Assert - Unmatched Shape Property Child (from OpenAPI spec baseline)
        var propCity = children.Single(c => c.Identifier == "GET /users/{id} -> $.address.city");
        Assert.That(propCity.IsVisited, Is.False);
        Assert.That(propCity.HitCount, Is.Zero);
    }

    [Test]
    public void WithCoverage_ShouldResolveOpenApiSpecificationFromClientConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .Add(new StaticConfigurationSource(new Dictionary<string, string?>
            {
                ["ProtoTest:Clients:TestApi:OpenApi:Specification"] = OpenApiTestHelper.SampleJsonSpec
            }))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        IProtoTargetBuilder targetBuilder = new ProtoRestTargetBuilder("TestApi", services);

        // Act
        targetBuilder.WithCoverage<OpenApiCoverageCollector>();
        using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<IProtoCollector>();

        // Assert
        Assert.That(collector, Is.TypeOf<OpenApiCoverageCollector>());
        Assert.That(collector.GetReportItems().Single().Identifier, Is.EqualTo("GET /users/{id}"));
    }

    private sealed class StaticConfigurationSource(IReadOnlyDictionary<string, string?> values) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new StaticConfigurationProvider(values);
    }

    private sealed class StaticConfigurationProvider(IReadOnlyDictionary<string, string?> values)
        : ConfigurationProvider
    {
        public override void Load()
        {
            Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        }
    }
}