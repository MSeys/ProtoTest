namespace ProtoTest.OpenApi.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Readers;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.OpenApi;

[TestFixture]
public class OpenApiCoverageCollectorTests
{
    private class DummyPayload { }

    [Test]
    public void Constructor_ShouldAcceptAPrebuiltOpenApiDocument()
    {
        var document = new OpenApiStringReader().Read(OpenApiTestHelper.SampleJsonSpec, out _);

        var collector = new OpenApiCoverageCollector("TestApi", document);

        var root = collector.GetReportItems().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Identifier, Is.EqualTo("GET /users/{id}"));
            Assert.That(root.Children!.Single(child => child.Identifier == "200")
                .Children!.Any(child => child.Identifier == "$.id"), Is.True);
        }
    }

    [Test]
    public void Collect_ShouldNormalizeIndexedPropertyPathsToArrayItems()
    {
        const string specification = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Lines", "version": "1" },
          "paths": {
            "/lines": {
              "get": {
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "lines": {
                              "type": "array",
                              "items": {
                                "type": "object",
                                "properties": { "sku": { "type": "string" } }
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;
        var collector = new OpenApiCoverageCollector("Lines", specification);

        collector.Collect(new ProtoObservation(
            "Lines",
            "http.contract.shape",
            "GET /lines",
            new RestShapeMatchData("GET /lines", ["$.lines[0]", "$.lines[0].sku"], typeof(DummyPayload), StatusCode: 200)));

        var properties = collector.GetReportItems().Single().Children!
            .Single(child => child.Identifier == "200").Children!;
        var line = properties.Single(item => item.Identifier == "$.lines[]");
        var sku = properties.Single(item => item.Identifier == "$.lines[].sku");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(line.IsCovered, Is.True);
            Assert.That(line.DisplayName, Is.EqualTo("lines › item"));
            Assert.That(sku.IsCovered, Is.True);
            Assert.That(sku.DisplayName, Is.EqualTo("lines › item › sku"));
            Assert.That(properties.Any(item => item.Identifier == "$.lines[0]"), Is.False);
        }
    }

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
            response.Should.HaveHttpStatus(System.Net.HttpStatusCode.OK)
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

    [Test]
    public void Collect_ShouldEnforceRouteParameterConstraints()
    {
        const string specification = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Users", "version": "1" },
          "paths": {
            "/users/{id:int}": {
              "get": {
                "responses": { "200": { "description": "ok" } }
              }
            }
          }
        }
        """;
        var collector = new OpenApiCoverageCollector("Users", specification);
        var headers = new Dictionary<string, string>();

        collector.Collect(new ProtoObservation(
            "Users", "http.response", "GET /users/abc",
            new RestResponseData("GET", "/users/abc", 200, "{}", headers)));
        collector.Collect(new ProtoObservation(
            "Users", "http.response", "GET /users/42",
            new RestResponseData("GET", "/users/42", 200, "{}", headers)));

        var endpoint = collector.GetReportItems().Single();
        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Identifier, Is.EqualTo("GET /users/{id:int}"));
            Assert.That(endpoint.Count, Is.EqualTo(1), "Only a numeric id covers an {id:int} route.");
        });
    }

    [Test]
    public void Collect_ShouldCoverTheRootWholeBodyPath()
    {
        const string specification = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Health", "version": "1" },
          "paths": {
            "/health": {
              "get": {
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": { "application/json": { "schema": { "type": "string" } } }
                  }
                }
              }
            }
          }
        }
        """;
        var collector = new OpenApiCoverageCollector("Health", specification);

        collector.Collect(new ProtoObservation(
            "Health",
            "http.contract.shape",
            "GET /health",
            new RestShapeMatchData("GET /health", ["$"], typeof(DummyPayload), StatusCode: 200)));

        var root = collector.GetReportItems().Single().Children!
            .Single(child => child.Identifier == "200").Children!
            .Single(child => child.Identifier == "$");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.DisplayName, Is.EqualTo("Response body"));
            Assert.That(root.IsCovered, Is.True, "A whole-body match must land on the $ baseline row.");
            Assert.That(root.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public void Collect_ShouldMatchPropertyPathsCaseInsensitively()
    {
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);

        collector.Collect(new ProtoObservation(
            "TestApi",
            "http.contract.shape",
            "GET /users/{id}",
            new RestShapeMatchData(
                "GET /users/{id}",
                ["$.ID"],
                typeof(DummyPayload),
                StatusCode: 200)));

        var id = collector.GetReportItems().Single().Children!
            .Single(child => child.Identifier == "200").Children!
            .Single(child => child.Identifier == "$.id");
        Assert.That(id.IsCovered, Is.True,
            "Schema extraction is ignore-case, so a hit reported as $.ID must land on $.id.");
    }

    [Test]
    public void EndpointHits_ShouldBeASnapshot()
    {
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);
        collector.Collect(new ProtoObservation(
            "TestApi", "http.response", "GET /users/{id}",
            new RestResponseData("GET", "/users/{id}", 200, "{}", new Dictionary<string, string>())));

        var snapshot = collector.EndpointHits;
        ((Dictionary<(string, string), int>)snapshot)[("GET", "/users/{id}")] = 99;

        Assert.That(collector.EndpointHits[("GET", "/users/{id}")], Is.EqualTo(1),
            "Mutating a snapshot must not change the collector's hits.");
    }

    [Test]
    public void Collect_ShouldIgnoreMethodsTheSpecDoesNotDescribe()
    {
        var collector = new OpenApiCoverageCollector("TestApi", OpenApiTestHelper.SampleJsonSpec);
        var headers = new Dictionary<string, string>();

        collector.Collect(new ProtoObservation(
            "TestApi", "http.response", "DELETE /users/{id}",
            new RestResponseData("DELETE", "/users/{id}", 200, "{}", headers)));
        collector.Collect(new ProtoObservation(
            "TestApi", "http.response", "GET /users/{id}",
            new RestResponseData("GET", "/users/{id}", 200, "{}", headers)));

        Assert.That(collector.EndpointHits, Has.Count.EqualTo(1),
            "Only the method the spec describes becomes an endpoint hit.");
        var endpoint = collector.GetReportItems().Single();
        Assert.That(endpoint.Count, Is.EqualTo(1));
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
