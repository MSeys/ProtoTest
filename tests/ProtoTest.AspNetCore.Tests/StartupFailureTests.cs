namespace ProtoTest.AspNetCore.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// The failure mode the brief asked about (feature-plan A3): an ASP.NET Core application whose
/// startup throws. The application's own exception must reach the test, the failed start must not
/// leave a half-started server behind, and the run must stay usable afterwards.
/// </summary>
[TestFixture]
public sealed class StartupFailureTests
{
    [Test]
    public async Task AddAspNetCoreServer_WhenTheApplicationThrowsAtStartup_ShouldFailSetupWithThatExceptionAndStayRetryable()
    {
        using var trace = new TemporaryTrace("startup-throw");
        var startup = new SwitchableStartupFilter();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = trace.Path);
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<SampleApi.Program>(
                configureWebHost: webHost => webHost.ConfigureTestServices(
                    services => services.AddSingleton<IStartupFilter>(startup)))
            .AddRest(rest => rest.AddClient("Api")));
        await using var host = builder.Build();
        await host.StartAsync();

        // Act: the application's pipeline build throws while the test's clients initialize.
        var application = new ApplicationAttribute("Api");
        var exception = Assert.ThrowsAsync<ApplicationStartupException>(async () =>
            await host.StartTestAsync("startup throw", "00001", TestMethods.Placeholder, [application]));

        // Assert: the original exception surfaced, the rollback left no ambient context, and the run
        // did not remember the failed start as a started server.
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ApplicationStartupException.StartupMessage),
                "the application's own exception reaches the caller unwrapped");
            Assert.Throws<InvalidOperationException>(() => _ = Proto.Context,
                "a failed setup leaves no ambient execution context");
        });

        // The failed start is retryable: the same host starts the same application once the startup
        // fault is gone, so no half-started server or client survived the rollback.
        startup.Fail = false;
        var context = await host.StartTestAsync("startup retry", "00002", TestMethods.Placeholder, [application]);
        using var response = await context.Rest().GetAsync("/ping");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var tests = host.Trace.Snapshot().Tests;
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            Assert.That(tests.Single(test => test.TestId == "00001").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Failed), "the throwing startup failed the test");
            Assert.That(tests.Single(test => test.TestId == "00001").Error!.Message,
                Is.EqualTo(ApplicationStartupException.StartupMessage), "the trace carries the application's error");
            Assert.That(
                tests.Single(test => test.TestId == "00001").Entries!
                    .Any(entry => entry.Kind == "aspnetcore.server.initialize"),
                Is.False,
                "no server initialize event was recorded for the failed start");
            Assert.That(tests.Single(test => test.TestId == "00002").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Succeeded));
        });
    }

    /// <summary>The application exception; the test asserts this exact instance identity by type.</summary>
    private sealed class ApplicationStartupException : Exception
    {
        public const string StartupMessage = "the application failed while its pipeline started";

        public ApplicationStartupException()
            : base(StartupMessage)
        {
        }
    }

    /// <summary>An application startup fault: the filter throws while the request pipeline is built.</summary>
    private sealed class SwitchableStartupFilter : IStartupFilter
    {
        public bool Fail { get; set; } = true;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            if (Fail)
            {
                throw new ApplicationStartupException();
            }

            next(app);
        };
    }
}
