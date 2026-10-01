namespace Starter.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.MSTest;
using ProtoTest.Reporting;
using ProtoTest.Rest;

/// <summary>
/// Composes the suite once for the whole run: the API hosted in-process, a REST client for it, coverage of
/// the endpoints the tests call, a trace of everything, and a JSON and an HTML report. MSTest calls the assembly hooks
/// once per test process.
/// </summary>
[TestClass]
public sealed class Setup : ProtoTestAssembly
{
    [AssemblyInitialize]
    public static Task AssemblyInitializeAsync(TestContext context) =>
        InitializeAsync(builder =>
            builder
                .AddApplication("Api", app => app
                    .AddAspNetCoreServer<Program>()
                    .AddRest(rest => rest
                        .AddClient("Api")
                        .AddCollector<RestCoverageCollector>()))
                .AddSink<JsonReportSink>(sink => sink.OutputPath = "TestResults/Starter.json")
                .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/Starter.html"));

    [AssemblyCleanup]
    public static Task AssemblyCleanupAsync() => CleanupAsync();
}
