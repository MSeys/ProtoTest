namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core.Internal;

/// <summary>
/// Pins the one environment-satisfaction evaluator (Audit 5 A5-13): the infrastructure-skip decision
/// and the capability-drop decision answer through the same keys, mode, configuration and
/// declared-keys rule, so the two semantics cannot drift apart.
/// </summary>
[TestFixture]
public sealed class EnvironmentVerdictTests
{
    private const string ConfiguredKey = "ConnectionStrings:Configured";
    private const string DeclaredOnlyKey = "ConnectionStrings:Declared";
    private const string MissingKey = "ConnectionStrings:Missing";
    private const string WhitespaceKey = "ConnectionStrings:Whitespace";

    private static readonly IReadOnlySet<string> DeclaredKeys =
        new HashSet<string>(StringComparer.Ordinal) { DeclaredOnlyKey };

    private static IConfiguration Configuration
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfiguredKey] = "Host=configured",
                [WhitespaceKey] = "   "
            })
            .Build();

    [Test]
    public void IsSatisfied_ShouldAnswerTheModeAcrossKeysValuesAndDeclaredKeys()
    {
        var configuration = Configuration;
        var verdicts = new (string Name, ProtoEnvironmentMode Mode, string[] Keys, bool Expected)[]
        {
            ("all configured", ProtoEnvironmentMode.AllConfigured, [ConfiguredKey], true),
            ("an empty value is not configured", ProtoEnvironmentMode.AllConfigured, [WhitespaceKey], false),
            ("one missing key fails all-configured", ProtoEnvironmentMode.AllConfigured, [ConfiguredKey, MissingKey], false),
            ("a declared-but-unconfigured key does not satisfy all-configured", ProtoEnvironmentMode.AllConfigured, [DeclaredOnlyKey], false),
            ("any provided by configuration", ProtoEnvironmentMode.AnyProvided, [ConfiguredKey], true),
            ("any provided by a declared key", ProtoEnvironmentMode.AnyProvided, [DeclaredOnlyKey], true),
            ("one provided key is enough", ProtoEnvironmentMode.AnyProvided, [ConfiguredKey, MissingKey], true),
            ("none provided", ProtoEnvironmentMode.AnyProvided, [MissingKey], false),
            ("no keys never satisfies all-configured", ProtoEnvironmentMode.AllConfigured, [], false),
            ("no keys never satisfies any-provided", ProtoEnvironmentMode.AnyProvided, [], false)
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, mode, keys, expected) in verdicts)
            {
                Assert.That(
                    ProtoEnvironment.IsSatisfied(configuration, keys, mode, DeclaredKeys),
                    Is.EqualTo(expected),
                    name);
            }
        }
    }

    [Test]
    public void IsProvided_ShouldCountConfigurationAndDeclaredKeys()
    {
        var configuration = Configuration;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ProtoEnvironment.IsProvided(configuration, ConfiguredKey, DeclaredKeys), Is.True);
            Assert.That(ProtoEnvironment.IsProvided(configuration, DeclaredOnlyKey, DeclaredKeys), Is.True);
            Assert.That(ProtoEnvironment.IsProvided(configuration, WhitespaceKey, DeclaredKeys), Is.False);
            Assert.That(ProtoEnvironment.IsProvided(configuration, MissingKey, DeclaredKeys), Is.False);
        }
    }

    [Test]
    public void TheInfrastructureSkipAndCapabilityDropDecisions_ShouldShareTheOneVerdict()
    {
        var configuration = Configuration;
        var registration = new ProtoInfrastructureRegistration(
            new StubInfrastructure(), [ConfiguredKey]);
        var capability = new ProtoConditionalCapability(
            new ProtoCapabilityDescriptor("Adapter", "protocol", "Tests"),
            new ProtoKeySet([ConfiguredKey]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                registration.IsSatisfiedBy(configuration, DeclaredKeys),
                Is.True,
                "a piece whose every key is configured is skipped");
            Assert.That(
                capability.IsDropped(configuration, DeclaredKeys),
                Is.EqualTo(registration.IsSatisfiedBy(configuration, DeclaredKeys)),
                "the capability drops on exactly the infrastructure-skip verdict");
        }

        var withAMissingKey = new ProtoInfrastructureRegistration(
            new StubInfrastructure(), [ConfiguredKey, MissingKey]);
        var unsatisfied = new ProtoConditionalCapability(
            new ProtoCapabilityDescriptor("Adapter", "protocol", "Tests"),
            new ProtoKeySet([ConfiguredKey, MissingKey]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withAMissingKey.IsSatisfiedBy(configuration, DeclaredKeys), Is.False);
            Assert.That(
                unsatisfied.IsDropped(configuration, DeclaredKeys),
                Is.EqualTo(withAMissingKey.IsSatisfiedBy(configuration, DeclaredKeys)),
                "a partially configured declaration drops on exactly the same verdict");
        }
    }

    private sealed class StubInfrastructure : IProtoInfrastructure
    {
        public string Id => "stub";

        public string Kind => "stub";

        public string Description => "Stub infrastructure";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
