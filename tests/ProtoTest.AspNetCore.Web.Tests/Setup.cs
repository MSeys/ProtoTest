namespace ProtoTest.AspNetCore.Web.Tests;

using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Testcontainers;
using ProtoTest.Web;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// Hosts the sample API twice: on its own loopback listener inside this process (the A5.7c recipe in
/// docs/docs/recipes/api-then-browser.md) and, when a container runtime is available, from its own
/// container image (A5.7d) - each published as its application's <c>BaseUrl</c>, so a browser session
/// and the REST client resolve one running application per name. Registering each piece with the key
/// it fills means a run that configures that key skips the piece and points at that environment
/// instead. A machine without a container runtime records the reason and the container journey skips.
/// </summary>
[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    internal const string LoopbackApplicationName = "Api";
    internal const string ContainerApplicationName = "ContainerApi";

    /// <summary>The pinned public ASP.NET Core sample image: it serves its page on port 8080 over HTTP.</summary>
    internal const string ContainerImage = "mcr.microsoft.com/dotnet/samples:aspnetapp";
    internal const int ContainerPort = 8080;

    /// <summary>Gets the started containerized application, or <see langword="null"/> without a runtime.</summary>
    internal static ApplicationContainer? ContainerApplication { get; private set; }

    /// <summary>Gets why the containerized application is unavailable, when it is.</summary>
    internal static string? ContainerError { get; private set; }

    protected override void Configure(IProtoHostBuilder builder)
    {
        var loopback = new LoopbackApplication(LoopbackApplicationName, SampleApi.Program.CreateApp);
        builder
            .AddInfrastructure(loopback, loopback.BaseUrlKey)
            .AddHttpReadiness(loopback.ApplicationName, "/welcome")
            .AddApplication(loopback.ApplicationName, app => app
                .AddRest(rest => rest.AddClient(loopback.ApplicationName))
                .AddWeb(options => options.Headless = true));

        // The container is started before the host so a machine without a container runtime can decide
        // to skip (TryStart) instead of failing the suite; the started container is then registered
        // like any other piece and released with the run.
        var started = ApplicationContainer.TryStart(
            ContainerApplicationName, ContainerImage, ContainerPort);
        ContainerApplication = started.Resource;
        ContainerError = started.Error;
        if (started.Resource is { } container)
        {
            builder
                .AddInfrastructure(container, container.BaseUrlKey)
                .AddHttpReadiness(container.Application, "/")
                .AddApplication(container.Application, app => app
                    .AddRest(rest => rest.AddClient(container.Application))
                    .AddWeb(options => options.Headless = true));
        }
    }
}
