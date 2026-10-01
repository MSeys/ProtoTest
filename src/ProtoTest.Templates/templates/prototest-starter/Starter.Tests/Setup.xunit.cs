namespace Starter.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.Xunit;
using Xunit;

/// <summary>
/// Composes the suite once for the whole run: the API hosted in-process, a REST client for it, coverage of
/// the endpoints the tests call, a trace of everything, and a JSON and an HTML report. xUnit builds the collection
/// fixture once per test process, before any test class runs.
/// </summary>
public sealed class ProtoTestFixture : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<Program>()
                .AddRest(rest => rest
                    .AddClient("Api")
                    .AddCollector<RestCoverageCollector>()))
            .AddSink<JsonReportSink>(sink => sink.OutputPath = "TestResults/Starter.json")
            .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/Starter.html");
}

/// <summary>
/// The collection every test class joins; it is what makes the fixture above run.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProtoTestCollection : ICollectionFixture<ProtoTestFixture>
{
    public const string Name = "ProtoTest Collection";
}
