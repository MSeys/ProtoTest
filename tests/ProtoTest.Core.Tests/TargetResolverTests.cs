namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core.Internal;

/// <summary>
/// The one resolution rule behind ordered target chains: providers are walked in order against the
/// resolved configuration, the first condition that holds wins, and every other provider is kept with
/// the reason it lost.
/// </summary>
[TestFixture]
public sealed class TargetResolverTests
{
    private const string StoreKey = "Store:Connection";

    [Test]
    public void Resolve_ShouldTakeTheFirstProviderWhoseConditionHolds()
    {
        var selected = new ProtoTargetProvider(
            "selected",
            Condition: ProtoProviderConditions.Selected("Selection:Enabled"));
        var fallback = new ProtoTargetProvider("fallback");
        var resolution = ResolveOne(Configuration(("Selection:Enabled", "true")), [selected, fallback]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolution.Winner, Is.SameAs(selected));
            Assert.That(resolution.Skipped, Has.Count.EqualTo(1));
            Assert.That(resolution.Skipped[0].Provider, Is.SameAs(fallback));
            Assert.That(
                resolution.Skipped[0].Reason,
                Does.Contain("earlier provider 'selected'"),
                "a provider after the winner is skipped for its position, whatever its own condition says");
        }
    }

    [Test]
    public void Resolve_ShouldRecordProvidersBeforeTheWinnerWithTheirUnmetCondition()
    {
        var configured = new ProtoTargetProvider("configured", Condition: ProtoProviderConditions.Configured);
        var fallback = new ProtoTargetProvider("fallback");
        var resolution = ResolveOne(Configuration(), [configured, fallback], StoreKey);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolution.Winner, Is.SameAs(fallback));
            Assert.That(resolution.Keys, Is.EqualTo(new[] { StoreKey }));
            Assert.That(resolution.Skipped[0].Provider, Is.SameAs(configured));
            Assert.That(
                resolution.Skipped[0].Reason,
                Does.Contain(StoreKey),
                "the skip reason names the missing key");
        }
    }

    [Test]
    public void Resolve_ShouldFailNamingTheTargetAndEveryUnmetCondition()
    {
        var configured = new ProtoTargetProvider("configured", Condition: ProtoProviderConditions.Configured);
        var aspire = new ProtoTargetProvider(
            "aspire",
            Condition: ProtoProviderConditions.Selected("ProtoTest:Aspire:Enabled"));
        var exception = Assert.Throws<ProtoTargetResolutionException>(
            () => ResolveOne(Configuration(), [configured, aspire], StoreKey));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.TargetName, Is.EqualTo("Store"));
            Assert.That(exception.Message, Does.Contain("No provider can serve target 'Store'"));
            Assert.That(exception.Message, Does.Contain($"missing: {StoreKey}"));
            Assert.That(
                exception.Message,
                Does.Contain("'aspire': The selection key 'ProtoTest:Aspire:Enabled' must be set"));
        }
    }

    [Test]
    public void Resolve_ShouldFailAnEmptyChainNamingTheFix()
    {
        var exception = Assert.Throws<ProtoTargetResolutionException>(
            () => ResolveOne(Configuration(), [], StoreKey));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("No provider is registered for target 'Store'"));
            Assert.That(exception.Message, Does.Contain("UseConfigured()"));
        }
    }

    [Test]
    public void Configured_ShouldHoldOnlyWhenEveryDeclaredKeyHasAValue()
    {
        var condition = ProtoProviderConditions.Configured;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                condition.IsSatisfied(Context(Configuration(("A", "1"), ("B", "2")), "A", "B")),
                Is.True);
            Assert.That(
                condition.IsSatisfied(Context(Configuration(("A", "1")), "A", "B")),
                Is.False,
                "one missing key leaves the target unconfigured");
            Assert.That(
                condition.IsSatisfied(Context(Configuration(("A", "  ")), "A")),
                Is.False,
                "a blank value is not a configured value");
            Assert.That(
                condition.IsSatisfied(Context(Configuration(), [])),
                Is.False,
                "a target that declares no key cannot be configured");
            Assert.That(
                condition.Describe(Context(Configuration(("A", "1")), "A", "B")),
                Does.Contain("missing: B"),
                "the requirement names the keys the environment lacks");
        }
    }

    [Test]
    public void Selected_ShouldHoldOnlyWhenTheSelectionKeyIsSet()
    {
        var condition = ProtoProviderConditions.Selected("Selection:Enabled");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(condition.IsSatisfied(Context(Configuration(("Selection:Enabled", "true")))), Is.True);
            Assert.That(condition.IsSatisfied(Context(Configuration())), Is.False);
            Assert.That(
                condition.Describe(Context(Configuration())),
                Is.EqualTo("The selection key 'Selection:Enabled' must be set"));
        }
    }

    [Test]
    public void Selected_ShouldHoldWhenAnyOfSeveralKeysIsSet()
    {
        var condition = ProtoProviderConditions.Selected("Aspire:Enabled", "Aspire:Resources:db:Enabled");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                condition.IsSatisfied(Context(Configuration(("Aspire:Enabled", "true")))),
                Is.True,
                "the broad selection key selects the provider");
            Assert.That(
                condition.IsSatisfied(Context(Configuration(("Aspire:Resources:db:Enabled", "true")))),
                Is.True,
                "the per-target selection key selects the provider on its own");
            Assert.That(
                condition.IsSatisfied(Context(Configuration(("Aspire:Enabled", "  ")))),
                Is.False,
                "a blank value is not a selection");
            Assert.That(
                condition.IsSatisfied(Context(Configuration())),
                Is.False);
            Assert.That(
                condition.Describe(Context(Configuration())),
                Is.EqualTo("One of the selection keys must be set: 'Aspire:Enabled', 'Aspire:Resources:db:Enabled'"));
        }
    }

    [Test]
    public void Available_ShouldFollowTheProbeAndNameItsRequirement()
    {
        var available = ProtoProviderConditions.Available("Docker is available", () => true);
        var unavailable = ProtoProviderConditions.Available("Docker is available", () => false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(available.IsSatisfied(Context(Configuration())), Is.True);
            Assert.That(unavailable.IsSatisfied(Context(Configuration())), Is.False);
            Assert.That(unavailable.Describe(Context(Configuration())), Is.EqualTo("Docker is available"));
        }
    }

    [Test]
    public void Always_ShouldHoldWithoutConfiguration()
    {
        Assert.That(ProtoProviderConditions.Always.IsSatisfied(Context(Configuration())), Is.True);
    }

    private static ProtoTargetResolution ResolveOne(
        IConfiguration configuration,
        IReadOnlyList<IProtoTargetProvider> providers,
        params string[] keys)
        => ProtoTargetResolver.Resolve(
            configuration,
            [new ProtoTargetChain("Store", keys, providers)]).Single();

    private static ProtoProviderConditionContext Context(IConfiguration configuration, params string[] keys)
        => new(configuration, keys);

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
