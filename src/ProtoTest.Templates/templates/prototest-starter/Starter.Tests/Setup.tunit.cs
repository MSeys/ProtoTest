using ProtoTest.TUnit;
using TUnit.Core.Executors;

[assembly: TestExecutor<ProtoTestExecutor>()]

namespace Starter.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.Rest;

/// <summary>
/// Composes the suite once for the whole run: the API hosted in-process, a REST client for it, coverage of
/// the endpoints the tests call, a trace of everything, and an HTML report. The assembly hooks start and
/// stop it around the run; the executor wraps every test in a ProtoTest context.
/// </summary>
public sealed class Setup : ProtoTestAssembly
{
    [Before(Assembly)]
    public static Task AssemblyInitializeAsync(AssemblyHookContext context) =>
        InitializeAsync(builder =>
            builder
                .AddApplication("Api", app => app
                    .AddAspNetCoreServer<Program>()
                    .AddRest(rest => rest
                        .AddClient("Api")
                        .AddCollector<RestCoverageCollector>()))
                .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/Starter.html"));

    [After(Assembly)]
    public static Task AssemblyCleanupAsync(AssemblyHookContext context) => CleanupAsync();
}
