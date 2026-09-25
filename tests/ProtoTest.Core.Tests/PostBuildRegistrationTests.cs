namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Pins the post-<c>Build()</c> registration split (audit Stage A0): tracing and resources throw,
/// while every <c>ConfigureServices</c>-based entry silently mutates a collection the built host no
/// longer reads.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class PostBuildRegistrationTests
{
    [Test]
    public async Task RegistrationAfterBuild_ShouldThrowForTracingAndResources_ButSilentlyNoOpForConfigureServicesEntries()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();

        // Pins current behavior; audit REG-3 gives every public builder entry one post-Build rule.
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(
                () => builder.ConfigureTracing(options => options.Enabled = false));
            Assert.Throws<InvalidOperationException>(() => builder.AddResource(new ProtoResource(
                "late-resource", "probe", "Late resource", _ => ValueTask.CompletedTask, ProtoResourceScope.Run)));
        });
        Assert.DoesNotThrow(
            () => builder.ConfigureServices(services => services.AddSingleton<LateService>()),
            "a service registered after Build is not rejected");
        Assert.DoesNotThrow(
            () => builder.AddTestHook<LateTestHook>(),
            "a hook registered after Build is not rejected");
        Assert.DoesNotThrow(
            () => builder.AddCapability(new ProtoCapabilityDescriptor("Late", ProtoCapabilityKinds.Protocol, "Tests")),
            "a capability registered after Build is not rejected");

        await host.StartAsync();
        await host.StartTestAsync("late registration", "00001", TestMethods.Placeholder);
        var lateService = Proto.Context.TryService<LateService>();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Protocol, "Late"),
                Is.False,
                "the late capability is invisible to the built host");
            Assert.That(lateService, Is.Null, "the late service is invisible to the built host");
        });
    }

    private sealed class LateService
    {
    }

    private sealed class LateTestHook : IProtoTestHook
    {
    }
}
