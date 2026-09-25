namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Pins how a descriptor declared conditionally under several keys is treated today: the build loop
/// decides per descriptor, so satisfying one declaration drops a capability another still promises.
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
    public async Task SameDescriptorUnderTwoKeys_WhenOnlyOneKeyIsConfigured_ShouldDropTheCapability()
    {
        var builder = BuilderWith(KeyA);
        builder.AddCapabilityUnlessConfigured(Capability, KeyA);
        builder.AddCapabilityUnlessConfigured(Capability, KeyB);

        await using var host = builder.Build();

        // Pins current behavior; audit REG-1 flips this once conditions are evaluated per declaration:
        // B's integration is still registered, so the descriptor must stay.
        Assert.That(
            host.HasCapability(Capability.Kind, Capability.Name),
            Is.False,
            "A's satisfied declaration removes the descriptor B's unsatisfied declaration still stands for");
    }

    [Test]
    public async Task SameDescriptorUnderTwoKeys_WhenBothKeysAreConfigured_ShouldDropTheCapability()
    {
        var builder = BuilderWith(KeyA, KeyB);
        builder.AddCapabilityUnlessConfigured(Capability, KeyA);
        builder.AddCapabilityUnlessConfigured(Capability, KeyB);

        await using var host = builder.Build();

        // Pins current behavior; audit REG-1 keeps this outcome once every declaration is satisfied.
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
