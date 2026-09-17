namespace ProtoTest.GraphQL.Tests;

using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using NUnit.Framework;

[TestFixture]
public sealed class ApplicationGraphQLTests
{
    [Test]
    public async Task Application_ShouldResolveTheDefaultGraphQlClient()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://app.test",
                ["ProtoTest:Applications:ControlPlane:Endpoints:GraphQL"] = "/graphql"
            }));
        builder.AddApplication("ControlPlane", app => app.AddGraphQL(graphql => graphql.AddClient("ControlPlane")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("graphql application", TestMethod(), [new ApplicationAttribute("ControlPlane")]);

        Assert.That(context.Client<HttpClient>("ControlPlane:ControlPlane").BaseAddress,
            Is.EqualTo(new Uri("https://app.test/graphql")));

        context.GraphQL();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "graphql.builder.create");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["client.name"], Is.EqualTo("ControlPlane"));
            Assert.That(entry.Attributes["application.name"], Is.EqualTo("ControlPlane"));
        });
    }

    private static MethodInfo TestMethod()
        => typeof(ApplicationGraphQLTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
