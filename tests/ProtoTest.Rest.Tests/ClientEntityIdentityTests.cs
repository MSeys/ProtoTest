namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.TestSupport;

/// <summary>
/// The request operation and the client's configuration link to one entity (the registry key the
/// resolver actually hit), so a REST request's trace carries one client entity, not a logical name and
/// a protocol-scoped name pointing at separate entities.
/// </summary>
[TestFixture]
public sealed class ClientEntityIdentityTests
{
    [Test]
    public async Task RestRequest_ShouldLinkToTheClientEntityThatOwnsTheConfiguration()
    {
        // Arrange
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Orders",
            "https://orders.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync("Identity", "20020", TestMethods.Placeholder);

        try
        {
            // Act
            using var response = await Proto.Context.Rest("Orders").GetAsync("/orders/1");
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        // Assert
        var test = host.Trace.Snapshot().Tests.Single();
        var clientStateIds = (test.Entities ?? [])
            .Where(entity => entity.Kind == ProtoTraceEntityKinds.Client)
            .Select(entity => entity.Id)
            .ToArray();
        var initializeEntity = test.Entries.Single(entry => entry.Kind == "client.initialize").EntityId;
        var requestEntity = test.Entries.Single(entry => entry.Kind == "http.request").EntityId;

        Assert.Multiple(() =>
        {
            Assert.That(
                clientStateIds,
                Does.Contain(initializeEntity),
                "the initialized client owns its configuration state");
            Assert.That(requestEntity, Is.Not.Null);
            Assert.That(
                requestEntity,
                Is.EqualTo(initializeEntity),
                "the request links to the entity the client's configuration was recorded on");
            Assert.That(clientStateIds, Does.Contain(requestEntity));
        });
    }

    [Test]
    public async Task ClientConfiguration_ShouldRedactCredentialsAndQueryParametersFromTheBaseAddress()
    {
        // The client's recorded address cannot carry user info or a
        // query parameter the redaction policy does not know.
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient("Orders", "https://user:pass@orders.test?access_token=secret"));
        await using var host = builder.Build();
        await host.StartTestAsync("Redaction", "20022", TestMethods.Placeholder);

        try
        {
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }

        var client = host.Trace.Snapshot().Tests.Single()
            .Entities!.Single(entity => entity.Kind == ProtoTraceEntityKinds.Client);
        var address = client.State["client.base_address"];
        Assert.Multiple(() =>
        {
            Assert.That(address, Does.Not.Contain("access_token"));
            Assert.That(address, Does.Not.Contain("secret"));
            Assert.That(address, Does.Not.Contain("pass"));
        });
    }
}
