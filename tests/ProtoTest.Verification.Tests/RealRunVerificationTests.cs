namespace ProtoTest.Verification.Tests;

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.OpenApi;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.TestSupport;

[TestFixture]
public sealed class RealRunVerificationTests
{
    private const string Spec = """
    {
      "openapi": "3.0.1",
      "info": { "title": "Verification", "version": "1.0.0" },
      "paths": {
        "/users/{id}": {
          "get": {
            "responses": {
              "200": {
                "description": "ok",
                "content": {
                  "application/json": {
                    "schema": {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "name": { "type": "string" }
                      }
                    }
                  }
                }
              },
              "404": { "description": "not found" }
            }
          }
        }
      }
    }
    """;

    [Test]
    public async Task Verify_ShouldCompareTwoRealRunsAndVerifyTheSameSpecification()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProtoTest.Verification.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var specPath = Path.Combine(directory, "openapi.json");
        await File.WriteAllTextAsync(specPath, Spec);
        try
        {
            var baseline = await RunAsync(directory, "baseline", specPath, assertName: true);
            var current = await RunAsync(directory, "current", specPath, assertName: false);

            var verdict = ProtoVerification.Verify(
                baseline, current, [new ProtoSpecCandidate("Api", specPath)]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(verdict.Failed, Is.True, "The current run stopped asserting the name property.");
                var finding = verdict.Findings.Single();
                Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.Regressed));
                Assert.That(finding.Severity, Is.EqualTo(ProtoVerificationSeverities.Fail));
                Assert.That(finding.TargetName, Is.EqualTo("Api"));
                Assert.That(finding.Category, Is.EqualTo("OpenAPI Property"));
                Assert.That(finding.Identifier, Is.EqualTo("$.name"));

                Assert.That(verdict.SpecChecks.Single().Status, Is.EqualTo(ProtoSpecCheckStatuses.Verified),
                    "The candidate file is the specification both runs recorded.");

                var properties = verdict.CoverageDeltas.Single(delta => delta.Category == "OpenAPI Property");
                Assert.That(properties.Baseline.Covered, Is.EqualTo(3));
                Assert.That(properties.Baseline.Total, Is.EqualTo(3));
                Assert.That(properties.Current.Covered, Is.EqualTo(2));
                Assert.That(properties.Regressed, Is.EqualTo(1));
                Assert.That(properties.PercentageDelta, Is.EqualTo(-33.33));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task Verify_ShouldReportAChangedCandidateSpecificationForARealRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProtoTest.Verification.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var specPath = Path.Combine(directory, "openapi.json");
        var changedPath = Path.Combine(directory, "openapi-changed.json");
        await File.WriteAllTextAsync(specPath, Spec);
        await File.WriteAllTextAsync(changedPath, Spec.Replace("\"name\"", "\"displayName\"", StringComparison.Ordinal));
        try
        {
            var baseline = await RunAsync(directory, "baseline", specPath, assertName: true);
            var current = await RunAsync(directory, "current", specPath, assertName: true);

            var verdict = ProtoVerification.Verify(
                baseline, current, [new ProtoSpecCandidate("Api", changedPath)]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(verdict.Failed, Is.True, "The candidate carries different specification bytes than the run used.");
                var finding = verdict.Findings.Single();
                Assert.That(finding.Class, Is.EqualTo(ProtoVerificationFindingClasses.StaleSpec));
                Assert.That(verdict.SpecChecks.Single().Status, Is.EqualTo(ProtoSpecCheckStatuses.Changed));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<ProtoVerificationRun> RunAsync(
        string directory,
        string name,
        string specPath,
        bool assertName)
    {
        var reportPath = Path.Combine(directory, $"{name}.json");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = Path.Combine(directory, $"{name}.prototrace"));
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:OpenApi:Specification"] = specPath
            }));
        builder.AddSink<JsonReportSink>(sink => sink.OutputPath = reportPath);
        builder.AddRest(rest => rest
            .AddClient("Api", "https://api.example.test", configure: http => http.ConfigurePrimaryHttpMessageHandler(() =>
                new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"42","name":"Ada"}""")
                })))
            .AddCollector<OpenApiCoverageCollector>());

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("journey", "1", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.Rest("Api").GetAsync("/users/{id}", new { id = 42 });
            if (assertName)
            {
                response.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(new { id = "42", name = "Ada" });
            }
            else
            {
                response.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(new { id = "42" });
            }
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        await host.StopAsync();
        return ProtoVerificationRun.FromReportFile(reportPath, name);
    }
}
