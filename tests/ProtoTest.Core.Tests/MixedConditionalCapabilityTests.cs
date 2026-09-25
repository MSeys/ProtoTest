namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Pins the per-declaration condition rule (audit REG-1): a descriptor stays while any conditional
/// declaration for it is unsatisfied, because the integration behind that declaration is still live.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class MixedConditionalCapabilityTests
{
    private static readonly ProtoCapabilityDescriptor Capability =
        new("Stub adapter", "protocol", "Tests");

    private const string KeyA = "ProtoTest:Applications:A:BaseUrl";
    private const string KeyB = "ProtoTest:Applications:B:BaseUrl";

    [Test]
    public async Task SameDescriptorUnderTwoKeys_WhenOnlyOneKeyIsConfigured_ShouldKeepTheCapability()
    {
        var builder = BuilderWith(KeyA);
        builder.AddCapabilityUnlessConfigured(Capability, KeyA);
        builder.AddCapabilityUnlessConfigured(Capability, KeyB);

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(Capability.Kind, Capability.Name),
            Is.True,
            "B's unsatisfied declaration still promises a live integration, so A's satisfied one cannot drop it");
    }

    [Test]
    public async Task SameDescriptorUnderTwoKeys_WhenBothKeysAreConfigured_ShouldDropTheCapability()
    {
        var builder = BuilderWith(KeyA, KeyB);
        builder.AddCapabilityUnlessConfigured(Capability, KeyA);
        builder.AddCapabilityUnlessConfigured(Capability, KeyB);

        await using var host = builder.Build();

        // Every declaration is satisfied, so no live integration needs the capability: it drops.
        Assert.That(host.HasCapability(Capability.Kind, Capability.Name), Is.False);
    }

    [Test]
    public async Task PlainDeclaration_WithASatisfiedConditionalForTheSameDescriptor_ShouldKeepTheCapability()
    {
        var builder = BuilderWith(KeyA);
        builder.AddCapabilityUnlessConfigured(Capability, KeyA);
        builder.AddCapability(Capability);

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(Capability.Kind, Capability.Name),
            Is.True,
            "an unconditional declaration is a promise the environment cannot withdraw");
    }

    private static ProtoHostBuilder BuilderWith(params string[] configuredKeys)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            configuredKeys.ToDictionary(key => key, _ => (string?)"configured")));
        return builder;
    }
}
