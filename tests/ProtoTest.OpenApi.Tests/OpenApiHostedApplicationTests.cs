namespace ProtoTest.OpenApi.Tests;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.OpenApi;
using ProtoTest.Rest;
using HostedApi = ProtoTest.OpenApi.TestApi.Program;

/// <summary>
/// OpenAPI coverage runs in the process that hosts the application, so the package must not change the
/// libraries the application loads. The hosted application documents itself with Swashbuckle 6, on the
/// Microsoft.OpenApi 1.6 line; it must start, serve its document and be covered.
/// </summary>
[TestFixture]
public sealed class OpenApiHostedApplicationTests
{
    [Test]
    public async Task Coverage_ShouldLeaveAnApplicationThatUsesSwashbuckleRunning()
    {
        var builder = new ProtoHostBuilder();
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<HostedApi>()
            .AddRest(rest => rest
                .AddClient("Api")
                .AddCollector<OpenApiCoverageCollector>(OpenApiTestHelper.SampleJsonSpec)));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "coverage next to Swashbuckle",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        using var document = await context.Rest().GetAsync("/swagger/v1/swagger.json");
        using var user = await context.Rest().GetAsync("/users/42");
        var endpoint = context.Services.GetServices<IProtoCollector>()
            .OfType<OpenApiCoverageCollector>()
            .Single()
            .GetReportItems()
            .Single(item => item.Identifier == "GET /users/{id}");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Swashbuckle serves the application's document");
            Assert.That(user.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(endpoint.Count, Is.EqualTo(1), "the call is covered against the contract");
        }
    }

    [Test]
    public void Package_ShouldNotReferenceAnOpenApiLibrary()
    {
        // Any Microsoft.OpenApi version the package referenced would replace the application's own one.
        var references = typeof(OpenApiCoverageCollector).Assembly.GetReferencedAssemblies().Select(name => name.Name);

        Assert.That(references, Has.None.StartsWith("Microsoft.OpenApi"));
    }
}
