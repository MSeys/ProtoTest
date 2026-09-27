namespace ProtoTest.WireMock.Tests;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.TestSupport;

public sealed class WireMockCoverageTests
{
    [Test]
    public async Task Coverage_ShouldMarkMatchedStubsCoveredAndKeepTheRestAsGaps()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWireMock();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("covered", "00001", TestMethods.Placeholder);

        var fake = Proto.Context.WireMock();
        fake.Stub(HttpMethod.Get, "/hit").RespondWith(HttpStatusCode.OK);
        fake.Stub(HttpMethod.Post, "/missed").RespondWith(HttpStatusCode.Created);
        using var http = new HttpClient();
        using (await http.GetAsync($"{fake.BaseUrl}/hit")) { }
        using (await http.GetAsync($"{fake.BaseUrl}/hit")) { }
        var collector = Proto.Context
            .Service<IEnumerable<IProtoCollector>>()
            .OfType<WireMockCoverageCollector>()
            .Single(collector => collector.TargetName == "WireMock:Default");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var items = collector.GetReportItems().ToArray();
        await host.StopAsync();

        var hit = items.Single(item => item.Identifier == "GET /hit");
        var missed = items.Single(item => item.Identifier == "POST /missed");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Length.EqualTo(2));
            Assert.That(hit.IsCovered, Is.True);
            Assert.That(hit.Count, Is.EqualTo(2));
            Assert.That(hit.Category, Is.EqualTo("WireMock"));
            Assert.That(missed.IsCovered, Is.False, "a stub no request matched stays a gap");
            Assert.That(missed.Count, Is.EqualTo(0));
        }
    }

    [Test]
    public void Collector_ShouldIgnoreOtherTargetsAndKinds()
    {
        var collector = new WireMockCoverageCollector("WireMock:Default");

        // The dispatcher only collects what CanCollect accepts; mirror that contract here.
        foreach (var observation in new[]
        {
            new ProtoObservation("WireMock:Default", ProtoWireMockProtocol.StubObservationKind, "GET /a"),
            new ProtoObservation("Rest:Default", ProtoWireMockProtocol.ResponseObservationKind, "GET /a"),
            new ProtoObservation("WireMock:Default", "wiremock.something-else", "GET /a")
        })
        {
            if (collector.CanCollect(observation))
            {
                collector.Collect(observation);
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                collector.CanCollect(new ProtoObservation(
                    "WireMock:Default", ProtoWireMockProtocol.ResponseObservationKind, "GET /a")),
                Is.True);
            Assert.That(
                collector.CanCollect(new ProtoObservation(
                    "Rest:Default", ProtoWireMockProtocol.ResponseObservationKind, "GET /a")),
                Is.False,
                "a REST target never feeds the fake's coverage");
            Assert.That(
                collector.CanCollect(new ProtoObservation(
                    "WireMock:Default", "wiremock.something-else", "GET /a")),
                Is.False);
            var items = collector.GetReportItems().ToArray();
            Assert.That(items, Has.Length.EqualTo(1), "only the stub registration collected");
            Assert.That(items[0].IsCovered, Is.False);
        }
    }

    [Test]
    public void Collector_ShouldKeepCoveredStubsCoveredWhenRegisteredAgain()
    {
        var collector = new WireMockCoverageCollector("WireMock:Default");

        collector.Collect(new ProtoObservation(
            "WireMock:Default", ProtoWireMockProtocol.StubObservationKind, "GET /a"));
        collector.Collect(new ProtoObservation(
            "WireMock:Default", ProtoWireMockProtocol.ResponseObservationKind, "GET /a"));
        collector.Collect(new ProtoObservation(
            "WireMock:Default", ProtoWireMockProtocol.StubObservationKind, "GET /a"));

        var item = collector.GetReportItems().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.IsCovered, Is.True, "a later registration never downgrades a hit stub");
            Assert.That(item.Count, Is.EqualTo(1));
        }
    }
}
