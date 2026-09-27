namespace ProtoTest.WireMock.Tests;

using System.Net;
using ProtoTest.Core;
using ProtoTest.TestSupport;

// The suite stays single-threaded: per-run fakes share one server, its stubs and request log across
// tests, and one test binds a fixed port. Per-test fakes isolate by construction (a fresh server per test).
public sealed class WireMockRoundTripTests
{
    [Test]
    public async Task StubMatchRoundTrip_ShouldServeTheStubbedBody()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("round trip", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        var stub = fake.Stub(HttpMethod.Get, "/orders/*").RespondJson(HttpStatusCode.OK, new { id = 42 });

        using var http = new HttpClient();
        using var response = await http.GetAsync($"{fake.BaseUrl}/orders/42");
        var body = await response.Content.ReadAsStringAsync();
        var received = stub.ReceivedCount;
        var served = fake.ReceivedRequests;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("42"));
            Assert.That(received, Is.EqualTo(1));
            Assert.That(served, Has.Count.EqualTo(1));
            Assert.That(served[0].Matched, Is.True);
        }
    }

    [Test]
    public async Task RawGiven_ShouldServeAndObserveWithTheConcretePath()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("raw given", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        fake.Given(global::WireMock.RequestBuilders.Request.Create()
                .WithPath("/raw")
                .UsingGet())
            .RespondWith(global::WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(200)
                .WithBody("raw-body"));

        using var http = new HttpClient();
        using var response = await http.GetAsync($"{fake.BaseUrl}/raw");
        var unmatched = fake.UnmatchedRequests;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("raw-body"));
            Assert.That(unmatched, Is.Empty);
        }
    }

    [Test]
    public async Task PerTestIsolation_ShouldStartANewServerWithoutThePreviousStubs()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        using var http = new HttpClient();

        await host.StartTestAsync("first", "00001", TestMethods.Placeholder);
        var first = Proto.Context.WireMock();
        first.Stub(HttpMethod.Get, "/orders/*").RespondJson(HttpStatusCode.OK, new { id = 1 });
        using (await http.GetAsync($"{first.BaseUrl}/orders/1")) { }
        var firstUrl = first.BaseUrl;
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("second", "00002", TestMethods.Placeholder);
        var second = Proto.Context.WireMock();
        using var leaked = await http.GetAsync($"{second.BaseUrl}/orders/1");
        var secondUrl = second.BaseUrl;
        var secondUnmatched = second.UnmatchedRequests;
        var secondServed = second.ReceivedRequests;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondUrl, Is.Not.EqualTo(firstUrl), "the second test gets a fresh server");
            Assert.That(leaked.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "the first test's stubs are gone");
            Assert.That(secondUnmatched, Has.Count.EqualTo(1));
            Assert.That(secondServed, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task UnmatchedRequest_ShouldFailNamingTheFakeMethodAndPath()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Payments");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("unmatched", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock("Payments");
        using var http = new HttpClient();
        using var response = await http.GetAsync($"{fake.BaseUrl}/nope");

        var exception = Assert.Throws<WireMockAssertionException>(() => fake.VerifyNoUnmatchedRequests());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(exception!.Message, Does.Contain("'Payments'"), "the failure names the fake");
            Assert.That(exception.Message, Does.Contain("GET /nope"), "the failure names the request");
        }
    }

    [Test]
    public async Task PerTestRelease_ShouldStopTheServerWithTheTest()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("release", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        var url = fake.BaseUrl;
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        using var http = new HttpClient();
        var stopped = Assert.ThrowsAsync<HttpRequestException>(async () => await http.GetAsync($"{url}/orders/1"));
        await host.StopAsync();

        Assert.That(stopped, Is.Not.Null, "the server stopped with the test");
    }

    [Test]
    public async Task PerRun_ShouldShareTheServerItsStubsAndItsLogAcrossTests()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Shared", fake => fake.PerRun());
        await using var host = builder.Build();
        await host.StartAsync();
        using var http = new HttpClient();

        await host.StartTestAsync("first", "00001", TestMethods.Placeholder);
        var first = Proto.Context.WireMock("Shared");
        first.Stub(HttpMethod.Get, "/a").RespondJson(HttpStatusCode.OK, new { n = 1 });
        using (await http.GetAsync($"{first.BaseUrl}/a")) { }
        var firstUrl = first.BaseUrl;
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("second", "00002", TestMethods.Placeholder);
        var second = Proto.Context.WireMock("Shared");
        using var stillServed = await http.GetAsync($"{second.BaseUrl}/a");
        second.Stub(HttpMethod.Get, "/b").RespondWith(HttpStatusCode.NoContent);
        using var served = await http.GetAsync($"{second.BaseUrl}/b");
        var secondUrl = second.BaseUrl;
        var logged = second.ReceivedRequests;
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var runningDuringRun = secondUrl == firstUrl;
        await host.StopAsync();
        var stopped = Assert.ThrowsAsync<HttpRequestException>(async () => await http.GetAsync($"{firstUrl}/a"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(runningDuringRun, Is.True, "both tests share one server");
            Assert.That(stillServed.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the first test's stub still matches");
            Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(logged, Has.Count.EqualTo(3), "the shared log keeps the run's requests");
            Assert.That(stopped, Is.Not.Null, "the shared server stops with the run");
        }
    }

    [Test]
    public async Task FixedPort_ShouldListenOnTheConfiguredPort()
    {
        var port = TestNetworking.FreePort();
        var builder = new ProtoHostBuilder();
        builder.AddWireMock("Fixed", fake => fake.Port(port));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("fixed port", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock("Fixed");
        fake.Stub(HttpMethod.Get, "/ping").RespondWith(HttpStatusCode.OK, "pong", "text/plain");
        using var http = new HttpClient();
        using var response = await http.GetAsync($"{fake.BaseUrl}/ping");
        var body = await response.Content.ReadAsStringAsync();
        var boundPort = fake.Port;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(boundPort, Is.EqualTo(port));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("pong"));
        }
    }

    [Test]
    public async Task Restub_ShouldReplaceTheMappingInsteadOfStacking()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("restub", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        var stub = fake.Stub(HttpMethod.Get, "/version").RespondJson(HttpStatusCode.OK, new { v = 1 });
        stub.RespondJson(HttpStatusCode.OK, new { v = 2 });

        using var http = new HttpClient();
        using var response = await http.GetAsync($"{fake.BaseUrl}/version");
        var body = await response.Content.ReadAsStringAsync();
        var received = stub.ReceivedCount;
        var mappings = fake.Server.Mappings.Count;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Contain("\"v\":2"));
            Assert.That(received, Is.EqualTo(1));
            Assert.That(mappings, Is.EqualTo(1), "one mapping serves the stub");
        }
    }
}
