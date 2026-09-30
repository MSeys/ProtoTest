using Starter.Tests;
using Xunit;

[assembly: AssemblyFixture(typeof(Setup))]

namespace Starter.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.Xunit3;

/// <summary>
/// Composes the suite once for the whole run: the API hosted in-process, a REST client for it, coverage of
/// the endpoints the tests call, a trace of everything, and an HTML report. The assembly fixture runs it
/// once per test process.
/// </summary>
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<Program>()
                .AddRest(rest => rest
                    .AddClient("Api")
                    .AddCollector<RestCoverageCollector>()))
            .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/Starter.html");
}
