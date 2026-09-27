namespace ProtoTest.Aspire.Tests;

using Microsoft.Extensions.Options;
using ProtoTest.Aspire.TestAppHost;
using ProtoTest.Core;

/// <summary>
/// The composition rules that need no orchestration runtime: registration shape, option validation
/// and the skip-condition errors.
/// </summary>
public sealed class AspireCompositionTests
{
    private sealed class OtherAppHostAnchor;

    [Test]
    public void AddAspireAppHost_WhenTheSameEntryPointMapsDifferentResources_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();
        builder.AddAspireAppHost<TestAppHostAnchor>("api");

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddAspireAppHost<OtherAppHostAnchor>("api"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'api'"), "the failure names the resource");
            Assert.That(exception.Message, Does.Contain(typeof(TestAppHostAnchor).FullName!));
            Assert.That(exception.Message, Does.Contain(typeof(OtherAppHostAnchor).FullName!));
        }
    }

    [Test]
    public void AspireOptions_ShouldRejectAnUnusableComposition()
    {
        var builder = new ProtoHostBuilder();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => builder.AddAspireAppHost<TestAppHostAnchor>("  ")),
                Is.Not.Null,
                "a blank resource name fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>([])),
                Is.Not.Null,
                "no resources fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(["api", "api"])),
                Is.Not.Null,
                "a repeated resource fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(
                        ["api"],
                        options => options.MapResource("worker", "Worker"))),
                Is.Not.Null,
                "mapping an unregistered resource fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(
                        ["api", "worker"],
                        options =>
                        {
                            options.MapResource("api", "App");
                            options.MapResource("worker", "App");
                        })),
                Is.Not.Null,
                "two resources sharing one application fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(
                        ["api"],
                        options => options.UseEndpoint("worker", "http"))),
                Is.Not.Null,
                "an endpoint for an unregistered resource fails");
        }
    }

    [Test]
    public void AspireOptions_WhenResourcesAreMapped_ShouldPublishUnderTheApplicationName()
    {
        var piece = new ProtoAspireAppHost<TestAppHostAnchor>(
            ["api"],
            options =>
            {
                options.MapResource("api", "Api");
                options.UseEndpoint("api", "https");
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(piece.ApplicationFor("api"), Is.EqualTo("Api"));
            Assert.That(piece.EndpointName("api"), Is.EqualTo("https"));
            Assert.That(piece.BaseUrlKey("api"), Is.EqualTo("ProtoTest:Applications:Api:BaseUrl"));
            Assert.That(piece.BaseUrlKeys, Is.EqualTo(new[] { "ProtoTest:Applications:Api:BaseUrl" }));
        }
    }

    [Test]
    public async Task AspireResource_WhenTheHostHasNoAppHost_ShouldNameTheRegistration()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("no apphost", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.AspireResource("api"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain("AddAspireAppHost"));
    }

    [Test]
    public async Task AspireResource_WhenTheResourceIsUnknown_ShouldListTheKnownOnes()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddAspireAppHost<TestAppHostAnchor>("api");
        await using var host = builder.Build();
        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException unavailable)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {unavailable.Message}");
            return;
        }

        await host.StartTestAsync("unknown resource", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.AspireResource("worker"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'worker'"));
            Assert.That(exception.Message, Does.Contain("'api'"));
        }
    }

    [Test]
    public void IsRuntimeMissing_ShouldRecognizeTheOrchestrationValidationFailure()
    {
        var missing = new OptionsValidationException(
            "Aspire",
            typeof(object),
            ["Property CliPath: The path to the DCP executable used for Aspire orchestration is required."]);
        var wrapped = new AggregateException(
            "The AppHost failed.",
            new InvalidOperationException("Inner start.", missing));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(missing),
                Is.True,
                "the DCP validation failure is a missing runtime");
            Assert.That(
                ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(wrapped),
                Is.True,
                "the failure is recognized through wrappers");
            Assert.That(
                ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(
                    new InvalidOperationException("The test AppHost was told to fail at start.")),
                Is.False,
                "an application failure is not a missing runtime");
        }
    }
}
