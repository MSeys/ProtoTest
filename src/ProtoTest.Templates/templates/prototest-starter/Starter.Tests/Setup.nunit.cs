namespace Starter.Tests;

using NUnit.Framework;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Reporting;
using ProtoTest.Rest;

/// <summary>
/// Composes the suite once for the whole run: the API hosted in-process, a REST client for it, coverage of
/// the endpoints the tests call, a trace of everything, and a JSON and an HTML report.
/// </summary>
[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
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
