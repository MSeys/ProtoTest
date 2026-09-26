namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Pins the provided-capability rule: a declaration drops when none of its keys can
/// provide the capability - no configured value and no registered infrastructure piece declares one -
/// and it composes with the other declaration kinds per declaration, not per descriptor.
/// </summary>
[TestFixture]
public sealed class ProvidedCapabilityTests
{
    private static readonly ProtoCapabilityDescriptor Capability =
        new("SQL", ProtoCapabilityKinds.Store, "Tests");

    private const string AddressKey = "ConnectionStrings:Orders";
    private const string SecondaryKey = "ConnectionStrings:Billing";
    private const string ConfiguredKey = "ProtoTest:Applications:Api:BaseUrl";

    [Test]
    public async Task AddCapabilityWhenProvided_WhenAKeyIsConfigured_ShouldKeepTheCapability()
    {
        var builder = BuilderWith(AddressKey);
        builder.AddCapabilityWhenProvided(Capability, AddressKey);

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(Capability.Kind, Capability.Name),
            Is.True,
            "a configured key can provide the capability");
    }

    [Test]
    public async Task AddCapabilityWhenProvided_WhenInfrastructureDeclaresTheKey_ShouldKeepTheCapability()
    {
        var builder = BuilderWith();
        builder.AddInfrastructure(new DeclaredSettingsInfrastructure("settings:orders", AddressKey, "Host=container"), AddressKey);
        builder.AddCapabilityWhenProvided(Capability, AddressKey);

        await using var host = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(host.Configuration[AddressKey], Is.Null, "the key has no configured value");
            Assert.That(
                host.HasCapability(Capability.Kind, Capability.Name),
                Is.True,
                "a registered piece that declares and fills the key can provide the capability before it starts");
        });
    }

    [Test]
    public async Task AddCapabilityWhenProvided_WhenNoKeyIsProvided_ShouldDropTheCapabilityAndNameTheKeys()
    {
        var output = Path.Combine(Path.GetTempPath(), $"prototest-provided-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddCapabilityWhenProvided(Capability, AddressKey, SecondaryKey);
            await using var host = builder.Build();
            await host.StartAsync();

            var skipped = host.Trace.Snapshot().Entries!.Single(entry => entry.Kind == "capability.skipped");
            await host.StopAsync();

            Assert.Multiple(() =>
            {
                Assert.That(host.HasCapability(Capability.Kind, Capability.Name), Is.False);
                Assert.That(skipped.Attributes["capability.keys"], Does.Contain(AddressKey));
                Assert.That(skipped.Attributes["capability.keys"], Does.Contain(SecondaryKey));
                Assert.That(skipped.Attributes["capability.reason"], Is.EqualTo("no key provided"));
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public void AddCapabilityWhenProvided_CalledTwiceWithTheSameKeys_ShouldRegisterOneDeclaration()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddCapabilityWhenProvided(Capability, AddressKey, SecondaryKey);
        builder.AddCapabilityWhenProvided(Capability, SecondaryKey, AddressKey);
        builder.AddCapabilityUnlessConfigured(Capability, AddressKey, SecondaryKey);

        var declarations = services!
            .Where(descriptor => descriptor.ServiceType == typeof(ProtoConditionalCapability))
            .Select(descriptor => (ProtoConditionalCapability)descriptor.ImplementationInstance!)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                declarations,
                Has.Length.EqualTo(2),
                "the same provided declaration merges; a different condition on the same keys stays its own declaration");
            Assert.That(
                declarations.Count(declaration => declaration.Condition == ProtoCapabilityCondition.WhenProvided),
                Is.EqualTo(1));
            Assert.That(
                declarations.Single(declaration => declaration.Condition == ProtoCapabilityCondition.WhenProvided).Keys.ToArray(),
                Is.EquivalentTo(new[] { AddressKey, SecondaryKey }),
                "keys are canonical: ordinal, distinct and order-independent");
        });
    }

    [Test]
    public async Task AddCapabilityWhenProvided_WithASatisfiedUnlessConfiguredDeclaration_ShouldDropTheCapability()
    {
        var builder = BuilderWith(ConfiguredKey);
        builder.AddCapabilityUnlessConfigured(Capability, ConfiguredKey);
        builder.AddCapabilityWhenProvided(Capability, AddressKey);

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(Capability.Kind, Capability.Name),
            Is.False,
            "every declaration says drop, so no live integration promises the capability");
    }

    [Test]
    public async Task AddCapabilityWhenProvided_WithAnUnprovidedUnlessConfiguredDeclaration_ShouldKeepTheCapability()
    {
        var builder = BuilderWith(AddressKey);
        builder.AddCapabilityUnlessConfigured(Capability, ConfiguredKey);
        builder.AddCapabilityWhenProvided(Capability, AddressKey);

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(Capability.Kind, Capability.Name),
            Is.True,
            "the unsatisfied unless-configured declaration still promises a live integration");
    }

    [Test]
    public async Task AddCapabilityWhenProvided_WithAPlainDeclaration_ShouldKeepTheCapability()
    {
        var builder = BuilderWith();
        builder.AddCapabilityWhenProvided(Capability, AddressKey);
        builder.AddCapability(Capability);

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(Capability.Kind, Capability.Name),
            Is.True,
            "a plain declaration is a promise no environment can withdraw");
    }

    [Test]
    public async Task AddCapabilityWhenProvided_OnAnApplicationBuilder_ShouldFollowTheSameRule()
    {
        var configured = BuilderWith(AddressKey);
        configured.AddApplication("Api", application => application.AddCapabilityWhenProvided(Capability, AddressKey));
        await using var configuredHost = configured.Build();
        Assert.That(
            configuredHost.HasCapability(Capability.Kind, Capability.Name),
            Is.True,
            "a configured key provides the capability an application declares");

        var unprovided = BuilderWith();
        unprovided.AddApplication("Api", application => application.AddCapabilityWhenProvided(Capability, AddressKey));
        await using var unprovidedHost = unprovided.Build();
        Assert.That(
            unprovidedHost.HasCapability(Capability.Kind, Capability.Name),
            Is.False,
            "the application overload drops the declaration under the same condition");
    }

    [Test]
    public void AddCapabilityWhenProvided_WithoutKeys_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => builder.AddCapabilityWhenProvided(Capability));
            Assert.Throws<ArgumentException>(() => builder.AddCapabilityWhenProvided(Capability, " "));
        });
    }

    private static ProtoHostBuilder BuilderWith(params string[] configuredKeys)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        if (configuredKeys.Length > 0)
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                configuredKeys.ToDictionary(key => key, _ => (string?)"configured")));
        }

        return builder;
    }
}
