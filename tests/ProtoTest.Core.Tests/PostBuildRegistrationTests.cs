namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Pins the one post-<c>Build()</c> rule: every public composition entry - on the host
/// builder and on an application builder - reports the same single message instead of silently
/// mutating a collection the built host no longer reads, and a second <c>Build()</c> reports that
/// same message.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class PostBuildRegistrationTests
{
    [Test]
    public async Task RegistrationAfterBuild_ShouldThrowForEveryCompositionEntry()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        IProtoApplicationBuilder? application = null;
        builder.AddApplication("Api", app => application = app);
        await using var host = builder.Build();

        var capability = new ProtoCapabilityDescriptor("Late", ProtoCapabilityKinds.Protocol, "Tests");
        var exceptions = new List<InvalidOperationException?>
        {
            Assert.Throws<InvalidOperationException>(
                () => builder.ConfigureServices(services => services.AddSingleton<LateService>())),
            Assert.Throws<InvalidOperationException>(
                () => builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection())),
            Assert.Throws<InvalidOperationException>(
                () => builder.ConfigureTestIds(options => options.RunPrefix = 123456)),
            Assert.Throws<InvalidOperationException>(
                () => builder.ConfigureReadiness(options => options.Timeout = TimeSpan.FromSeconds(1))),
            Assert.Throws<InvalidOperationException>(() => builder.Build()),
            Assert.Throws<InvalidOperationException>(() => builder.AddTestHook<LateTestHook>()),
            Assert.Throws<InvalidOperationException>(() => builder.AddRunHook<LateRunHook>()),
            Assert.Throws<InvalidOperationException>(() => builder.AddRunGate<LateRunGate>()),
            Assert.Throws<InvalidOperationException>(() => builder.AddRunGate("late", _ => ProtoRunGateResult.Passed())),
            Assert.Throws<InvalidOperationException>(() => builder.AddCapability(capability)),
            Assert.Throws<InvalidOperationException>(
                () => builder.AddCapabilityUnlessConfigured(capability, "Late:Key")),
            Assert.Throws<InvalidOperationException>(() => builder.ConfigureClock(new ProtoClock())),
            Assert.Throws<InvalidOperationException>(() => builder.AddSink(new CountingSink())),
            Assert.Throws<InvalidOperationException>(() => builder.AddInfrastructure(
                "late",
                chain => chain.Use(new ProtoTargetProvider("late", new LateInfrastructure())))),
            Assert.Throws<InvalidOperationException>(() => builder.AddResource(new ProtoResource(
                "late-resource", "probe", "Late resource", _ => ValueTask.CompletedTask, ProtoResourceScope.Run))),
            Assert.Throws<InvalidOperationException>(() => builder.AddApplication("Late", _ => { })),
            Assert.Throws<InvalidOperationException>(() => application!.AddCapability(capability)),
            Assert.Throws<InvalidOperationException>(() => application!.RegisterClient("Rest", "Late"))
        };

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Has.All.Not.Null, "every entry is rejected, not silently ignored");
            Assert.That(
                exceptions.Select(exception => exception!.Message).Distinct(),
                Is.EqualTo(new[] { ProtoHostBuilder.BuiltMessage }),
                "one message for every entry");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Protocol, "Late"),
                Is.False,
                "no late capability reaches the built host");
        });
    }

    [Test]
    public async Task ServiceRegisteredAfterBuild_ShouldNeverBeResolvable()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        IProtoApplicationBuilder? application = null;
        builder.AddApplication("Api", app => application = app);
        await using var host = builder.Build();

        Assert.Throws<InvalidOperationException>(
            () => builder.ConfigureServices(services => services.AddSingleton<LateService>()));
        // A captured application builder still hands out its service collection; a late mutation is
        // invisible to the built host instead of reaching it after composition.
        application!.Services.AddSingleton<LateService>();

        await host.StartAsync();
        await host.StartTestAsync("late registration", "00001", TestMethods.Placeholder);
        var lateService = Proto.Context.TryService<LateService>();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(lateService, Is.Null, "the late service is invisible to the built host");
    }

    private sealed class LateService
    {
    }

    private sealed class LateTestHook : IProtoTestHook
    {
    }

    private sealed class LateRunHook : IProtoRunHook
    {
    }

    private sealed class LateRunGate : IProtoRunGate
    {
        public string Name => "late";

        public ProtoRunGateResult Evaluate(ProtoRunGateContext context) => ProtoRunGateResult.Passed();
    }

    private sealed class LateInfrastructure : IProtoInfrastructure
    {
        public string Id => "late:infrastructure";
        public string Kind => "probe";
        public string Description => "Late infrastructure";
        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
