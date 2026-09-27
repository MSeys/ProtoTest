namespace ProtoTest.WireMock.Tests;

using System.Net;
using ProtoTest.Core;
using ProtoTest.TestSupport;

public sealed class WireMockRegistrationTests
{
    [Test]
    public async Task AddWireMock_ShouldDeclareTheCapabilityWithTheFakeAsTheInstance()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Payments");
        await using var host = builder.Build();
        await host.StartAsync();

        var capability = host.HasCapability(ProtoCapabilityKinds.Protocol, "WireMock");
        var instance = host.HasCapability(ProtoCapabilityKinds.Protocol, "WireMock", "Payments");

        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capability, Is.True);
            Assert.That(instance, Is.True, "the capability carries the fake as its instance");
        }
    }

    [Test]
    public async Task AddWireMock_WhenRepeatedWithEqualSettings_ShouldCompose()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Payments");
        builder.AddWireMock("Payments");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("composed", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock("Payments");
        fake.Stub(HttpMethod.Get, "/ping").RespondWith(HttpStatusCode.OK);
        var url = fake.BaseUrl;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(url, Does.StartWith("http"), "one fake serves the repeated registration");
    }

    [Test]
    public void AddWireMock_WhenRepeatedWithDifferentSettings_ShouldThrowNamingTheFake()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Payments");
        builder.ConfigureTracing(options => options.Enabled = false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            builder.AddWireMock("Payments", fake => fake.PerRun());
            builder.Build();
        });

        Assert.That(exception!.Message, Does.Contain("'Payments'"));
    }

    [Test]
    public async Task WireMock_WithoutRegistration_ShouldExplain()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("unregistered", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.WireMock());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain("AddWireMock"));
    }

    [Test]
    public async Task WireMock_WithSeveralFakes_ShouldAskForTheName()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Payments");
        builder.AddWireMock("Shipping");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("ambiguous", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.WireMock());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'Payments'").And.Contains("'Shipping'"));
        }
    }

    [Test]
    public async Task WireMock_ForAnUnknownName_ShouldNameTheKnownFakes()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Payments");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("unknown", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.WireMock("Fraud"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'Fraud'"));
            Assert.That(exception.Message, Does.Contain("'Payments'"));
        }
    }

    [Test]
    public void AddWireMock_WhenThePortIsOutOfRange_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddWireMock("Low", fake => fake.Port(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddWireMock("High", fake => fake.Port(65536)));
        });
    }

    [Test]
    public async Task Stub_WhenThePathIsNotAPath_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("bad stub", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => fake.Stub(HttpMethod.Get, "no-leading-slash"));
            Assert.Throws<ArgumentException>(() => fake.Stub("  ", "/ok"));
        });
    }

    [Test]
    public async Task VerifyHappened_WhenTheStubServedNothing_ShouldNameTheStub()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("unhit", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        var stub = fake.Stub(HttpMethod.Delete, "/orders/*").RespondWith(HttpStatusCode.NoContent);

        var exception = Assert.Throws<WireMockAssertionException>(() => stub.VerifyHappenedOnce());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("DELETE /orders/*"));
            Assert.That(exception.Message, Does.Contain("'Default'"));
        }
    }
}
