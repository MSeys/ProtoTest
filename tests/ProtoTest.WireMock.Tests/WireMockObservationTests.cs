namespace ProtoTest.WireMock.Tests;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.TestSupport;

public sealed class WireMockObservationTests
{
    [Test]
    public async Task MatchedRequest_ShouldRecordTheRestResponseShape()
    {
        var captured = new CapturingCollector();
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(captured));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("observed", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        fake.Stub(HttpMethod.Get, "/orders/*").RespondJson(HttpStatusCode.OK, new { id = 7 });
        using var http = new HttpClient();
        using (await http.GetAsync($"{fake.BaseUrl}/orders/7")) { }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var observations = host.Trace.Snapshot().Tests.Single().Record!.Observations!;
        await host.StopAsync();

        var response = observations.Single(observation =>
            observation.Kind == ProtoWireMockProtocol.ResponseObservationKind);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.TargetName, Is.EqualTo("WireMock:Default"));
            Assert.That(response.Identifier, Is.EqualTo("GET /orders/*"), "the stub template is the identifier");
            Assert.That(response.Data, Does.Contain("\"StatusCode\":200"), "the wire shape carries the status");
            Assert.That(response.Data, Does.Contain("orders/7"), "the wire shape carries the request URI");
            var shape = captured.OfKind<RestResponseData>(ProtoWireMockProtocol.ResponseObservationKind).Single();
            Assert.That(shape.StatusCode, Is.EqualTo(200), "the CLR shape is the REST response shape");
            Assert.That(shape.Method, Is.EqualTo("GET"));
            Assert.That(shape.RouteTemplate, Is.EqualTo("/orders/*"));
            Assert.That(shape.ResponseBody, Does.Contain("7"));
        }
    }

    [Test]
    public async Task UnmatchedRequest_ShouldRecordTheRestFailureShapeWithoutCoverage()
    {
        var captured = new CapturingCollector();
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(captured));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("unmatched", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        using var http = new HttpClient();
        using (await http.GetAsync($"{fake.BaseUrl}/missing")) { }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var observations = host.Trace.Snapshot().Tests.Single().Record!.Observations!;
        await host.StopAsync();

        var failure = observations.Single(observation =>
            observation.Kind == ProtoWireMockProtocol.FailureObservationKind);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Identifier, Is.EqualTo("GET /missing"));
            Assert.That(failure.Data, Does.Contain("WireMockUnmatchedRequest"));
            Assert.That(failure.Data, Does.Contain("GET /missing"));
            var shape = captured.OfKind<RestFailureData>(ProtoWireMockProtocol.FailureObservationKind).Single();
            Assert.That(shape.Message, Does.Contain("GET /missing"), "the CLR shape names the request");
            Assert.That(
                observations.Any(observation =>
                    observation.Kind == ProtoWireMockProtocol.ResponseObservationKind),
                Is.False,
                "an unmatched request is never a response observation");
        }
    }

    [Test]
    public async Task StubRegistration_ShouldRecordTheStubObservation()
    {
        var captured = new CapturingCollector();
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(captured));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("stubbed", "00001", TestMethods.Placeholder);

        Proto.Context.WireMock().Stub(HttpMethod.Post, "/orders").RespondWith(HttpStatusCode.Created);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var stub = captured.OfKind<WireMockStubData>(ProtoWireMockProtocol.StubObservationKind).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stub.Method, Is.EqualTo("POST"));
            Assert.That(stub.Path, Is.EqualTo("/orders"));
        }
    }

    [Test]
    public async Task ServerStart_ShouldRecordTheServerEntity()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("entity", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        var url = fake.BaseUrl;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var entities = host.Trace.Snapshot().Tests.Single().Entities!;
        await host.StopAsync();

        var server = entities.Single(entity => entity.Kind == ProtoTraceEntityKinds.Server);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(server.Id, Is.EqualTo("server:WireMock:Default"));
            Assert.That(server.State["server.url"], Is.EqualTo(url));
            Assert.That(server.State["server.lifetime"], Is.EqualTo("test"));
        }
    }

    private sealed class CapturingCollector : IProtoCollector
    {
        private readonly ProtoLock _gate = new();
        private readonly List<ProtoObservation> _observations = [];

        public bool CanCollect(ProtoObservation observation) => true;

        public void Collect(ProtoObservation observation)
        {
            lock (_gate)
            {
                _observations.Add(observation);
            }
        }

        public IReadOnlyList<T> OfKind<T>(string kind)
        {
            lock (_gate)
            {
                return [.. _observations
                    .Where(observation =>
                        observation.TargetName == "WireMock:Default" && observation.Kind == kind)
                    .Select(observation => observation.Data)
                    .OfType<T>()];
            }
        }
    }
}
