namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using System.Reflection;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// The low-ceremony one-liner: one call composes the application in-process under the default name,
/// and the application's protocols compose in the same call.
/// </summary>
public sealed class ProtoTestHostTests
{
    [Application(ProtoTestHost.DefaultApplicationName)]
    private static void DefaultApplicationMethod()
    {
    }

    [Application("OneLinerApi")]
    private static void NamedApplicationMethod()
    {
    }

    [Test]
    public async Task For_ShouldHostTheApplicationInProcessUnderTheDefaultName()
    {
        var host = ProtoTestHost.For<SampleApi.Program>(new ProtoHostBuilder()).Build();
        await using var ownedHost = host;

        var method = Subject(nameof(DefaultApplicationMethod));
        await host.StartTestAsync("OneLiner_Default", "00020", method, ProtoAttributeResolver.Resolve(method));
        try
        {
            // The application's HTTP clients reuse the in-process transport even with no REST client
            // registered for it, and the server is reachable by the default name.
            using var response = await Proto.Context.Rest().GetAsync("/ping");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(host.HasApplication(ProtoTestHost.DefaultApplicationName), Is.True);
                Assert.That(
                    host.HasCapability(ProtoCapabilityKinds.Server, null, ProtoTestHost.DefaultApplicationName),
                    Is.True,
                    "the one-liner registers the in-process server under the application name");
                Assert.That(Proto.Context.ServerFactory<SampleApi.Program>(), Is.Not.Null);
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }
    }

    [Test]
    public async Task For_ShouldComposeApplicationProtocolRegistrations()
    {
        var host = ProtoTestHost.For<SampleApi.Program>(
                new ProtoHostBuilder(),
                "OneLinerApi",
                application => application.AddRest(rest => rest.AddClient()))
            .Build();
        await using var ownedHost = host;

        var method = Subject(nameof(NamedApplicationMethod));
        await host.StartTestAsync("OneLiner_Named", "00021", method, ProtoAttributeResolver.Resolve(method));

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/ping");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(host.HasApplication("OneLinerApi"), Is.True);
                Assert.That(
                    host.HasCapability(ProtoCapabilityKinds.Server, null, "OneLinerApi"),
                    Is.True,
                    "the named application owns its in-process server");
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }
    }

    [Test]
    public void For_ShouldRejectAMissingBuilderAndABlankName()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => ProtoTestHost.For<SampleApi.Program>(null!));
            Assert.Throws<ArgumentException>(() => ProtoTestHost.For<SampleApi.Program>(new ProtoHostBuilder(), " "));
        });
    }

    private static MethodInfo Subject(string name)
        => typeof(ProtoTestHostTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
}
