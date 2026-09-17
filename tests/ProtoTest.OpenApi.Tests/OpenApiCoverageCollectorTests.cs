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
        Assert.That(root.IsCovered, Is.False);
        Assert.That(root.Count, Is.Zero);

        var children = root.Children!;
        var response200 = children.Single(c => c.Identifier == "200");
        Assert.That(response200.IsCovered, Is.False);
        Assert.That(children.Any(c => c.Identifier == "404" && c.IsCovered is false), Is.True);
        Assert.That(response200.Children!.Any(c => c.Identifier == "$.id" && c.IsCovered is false), Is.True);
        Assert.That(root.Metadata, Is.Null);
    }

    [Test]
    public void Collect_ShouldIncrementCount_ForEndpointStatusAndShapeProperties()
    {
        // Arrange
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);
        var emptyHeaders = new Dictionary<string, string>();

        // Act - 1. Record HTTP Hits (verstuurd via RestRequestBuilder)
        collector.Collect(new ProtoObservation(
            TargetName: "TestApi",
            Kind: "http.response",
            Identifier: "GET /users/{id}",
            Data: new RestResponseData("GET", "/users/{id}", 200, "{}", emptyHeaders)
        ));

        collector.Collect(new ProtoObservation(
            TargetName: "TestApi",
            Kind: "http.response",
            Identifier: "GET /users/{id}",
            Data: new RestResponseData("GET", "/users/{id}", 200, "{}", emptyHeaders)
        ));

        // Act - 2. Record ShapeMatch Hit (verstuurd via RestResponse.ShouldMatchShape)
        // RequestIdentifier is the full request identifier ("GET /users/{id}").
        collector.Collect(new ProtoObservation(
            TargetName: "TestApi",
            Kind: "http.contract.shape",
            Identifier: "GET /users/{id}",
            Data: new RestShapeMatchData(
                RequestIdentifier: "GET /users/{id}",
                MatchedProperties: new[] { "$.id", "$.name" },
                TargetType: typeof(DummyPayload),
                StatusCode: 200
            )
        ));

        var items = collector.GetReportItems().ToList();

        // Assert - Root Item Checks
        var root = items.Single(i => i.Identifier == "GET /users/{id}");
        Assert.That(root.IsCovered, Is.True);
        Assert.That(root.Count, Is.EqualTo(2));

        var children = root.Children!;

        // Assert - Status Code Child
        var status200 = children.Single(c => c.Identifier == "200");
        Assert.That(status200.IsCovered, Is.True);
        Assert.That(status200.Count, Is.EqualTo(2));

        // Assert - Matched Shape Property Child
        var propId = status200.Children!.Single(c => c.Identifier == "$.id");
        Assert.That(propId.IsCovered, Is.True);
        Assert.That(propId.Count, Is.EqualTo(1));

        // Assert - Unmatched Shape Property Child (from OpenAPI spec baseline)
        var propCity = status200.Children!.Single(c => c.Identifier == "$.address.city");
        Assert.That(propCity.IsCovered, Is.False);
        Assert.That(propCity.Count, Is.Zero);
    }

    [Test]
    public void Collect_ShouldMatchAbsoluteConcreteRoutesAndIgnoreQueryStrings()
    {
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);

        collector.Collect(new ProtoObservation(
            "TestApi",
            "http.response",
            "GET https://api.example.test/users/42?expand=address",
            new RestResponseData(
                "GET",
                "https://api.example.test/users/42?expand=address",
                200,
                "{}",
                new Dictionary<string, string>())));

        var endpoint = collector.GetReportItems().Single();
        Assert.That(endpoint.IsCovered, Is.True);
        Assert.That(endpoint.Children!.Single(child => child.Identifier == "200").IsCovered, Is.True);
    }

    [Test]
    public void Collect_ShouldKeepPropertiesScopedToTheObservedResponse()
    {
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);

        collector.Collect(new ProtoObservation(
            "TestApi",
            "http.contract.shape",
            "GET /users/{id}",
            new RestShapeMatchData(
                "GET /users/{id}",
                ["$.id"],
                typeof(DummyPayload),
                StatusCode: 404)));

        var successResponse = collector.GetReportItems().Single().Children!
            .Single(child => child.Identifier == "200");
        Assert.That(successResponse.Children!.Single(child => child.Identifier == "$.id").IsCovered, Is.False);
    }

    [Test]
    public void Collect_ShouldMapWildcardAndDefaultResponses()
    {
        const string specification = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Responses", "version": "1" },
          "paths": {
            "/jobs": {
              "post": {
                "responses": {
                  "2XX": { "description": "accepted" },
                  "default": { "description": "other" }
                }
              }
            }
          }
        }
        """;
        var collector = new OpenApiCoverageCollector("Jobs", specification);
        var headers = new Dictionary<string, string>();

        collector.Collect(new ProtoObservation(
            "Jobs", "http.response", "POST /jobs",
            new RestResponseData("POST", "/jobs", 202, "", headers)));
        collector.Collect(new ProtoObservation(
            "Jobs", "http.response", "POST /jobs",
            new RestResponseData("POST", "/jobs", 503, "", headers)));

        var responses = collector.GetReportItems().Single().Children!;
        Assert.That(responses.Single(item => item.Identifier == "2XX").Count, Is.EqualTo(1));
        Assert.That(responses.Single(item => item.Identifier == "default").Count, Is.EqualTo(1));
    }

    [Test]
    public void AddCollector_ShouldResolveOpenApiSpecificationFromClientConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .Add(new StaticConfigurationSource(new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:TestApi:OpenApi:Specification"] = OpenApiTestHelper.SampleJsonSpec
            }))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        IProtoTargetBuilder targetBuilder = new TestTargetBuilder("TestApi", services);

        // Act
        targetBuilder.AddCollector<OpenApiCoverageCollector>();
        using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<IProtoCollector>();

        // Assert
        Assert.That(collector, Is.TypeOf<OpenApiCoverageCollector>());
        Assert.That(((IProtoReportSource)collector).GetReportItems().Single().Identifier, Is.EqualTo("GET /users/{id}"));
    }

    [Test]
    public async Task Collector_ShouldSeeObservationsUnderTheApplicationsQualifiedTargetName()
    {
        // Arrange: under an application the client is registered as "TestApi:Api", so observations must
        // carry that target name or every collector keyed on it goes blind.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"42","name":"Ada","address":{"city":"Ghent"}}""")
        });
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:TestApi:BaseUrl"] = "https://api.example.test",
                ["ProtoTest:Applications:TestApi:OpenApi:Specification"] = OpenApiTestHelper.SampleJsonSpec
            }));
        builder.AddApplication("TestApi", app => app.AddRest(rest => rest
            .AddClient("Api", configure: http => http.ConfigurePrimaryHttpMessageHandler(() => handler))
            .AddCollector<RestCoverageCollector>()
            .AddCollector<OpenApiCoverageCollector>()));
        await using var host = builder.Build();
        await host.StartTestAsync("application coverage", "10", TestMethod(), [new ApplicationAttribute("TestApi")]);
        try
        {
            // Act
            using var response = await Proto.Context.Rest().GetAsync("/users/{id}", new { id = 42 });
            response.ShouldHaveHttpStatus(System.Net.HttpStatusCode.OK)
                .ShouldMatchShape(new { id = "42", name = "Ada" });

            // Assert
            var collectors = Proto.Context.Services.GetServices<IProtoCollector>().ToArray();
            var openApi = collectors.OfType<OpenApiCoverageCollector>().Single();
            var rest = collectors.OfType<RestCoverageCollector>().Single();
            var endpoint = openApi.GetReportItems().Single(item => item.Identifier == "GET /users/{id}");
            var property = endpoint.Children!
                .Single(child => child.Identifier == "200").Children!
                .Single(child => child.Identifier == "$.id");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(endpoint.IsCovered, Is.True);
                Assert.That(endpoint.Count, Is.GreaterThan(0));
                Assert.That(property.IsCovered, Is.True);
                Assert.That(rest.GetReportItems().Single().Identifier, Is.EqualTo("GET /users/{id}"));
            }
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    private static System.Reflection.MethodInfo TestMethod()
        => typeof(OpenApiCoverageCollectorTests).GetMethod(
            nameof(TestPlaceholder), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static void TestPlaceholder()
    {
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class StaticConfigurationSource(IReadOnlyDictionary<string, string?> values) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new StaticConfigurationProvider(values);
    }

    private sealed class TestTargetBuilder(string targetName, IServiceCollection services)
        : IProtoTargetBuilder
    {
        public string TargetName { get; } = targetName;
        public IServiceCollection Services { get; } = services;
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
