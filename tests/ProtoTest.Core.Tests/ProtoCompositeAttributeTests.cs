namespace ProtoTest.Core.Tests;

using System.Reflection;

/// <summary>
/// Composite attributes: one declaration that stands for the attributes it declares. The framework
/// expands them where attributes are resolved, the composed attributes run at their own order, a
/// declaration that is both explicit and composed runs once, and the trace records the expansion.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ProtoCompositeAttributeTests
{
    private static readonly ProtoHost Host = new ProtoHostBuilder().Build();

    [OneTimeTearDown]
    public static async Task DisposeHost() => await Host.DisposeAsync();

    [Test]
    public void Resolve_ShouldExpandAClassLevelCompositeBeforeItself()
    {
        var method = GetMethod<ClassCompositeCases>(nameof(ClassCompositeCases.Case));

        var attributes = ProtoAttributeResolver.Resolve(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                attributes.Select(attribute => attribute.GetType().Name),
                Is.EqualTo(new[] { nameof(MarkerAttribute), nameof(NamedCompositeAttribute) }));
            Assert.That(
                attributes.OfType<MarkerAttribute>().Single().Name,
                Is.EqualTo("class plan-marker"),
                "the composite's arguments flow into the attribute it declares");
        }
    }

    [Test]
    public void Resolve_ShouldExpandAMethodLevelComposite()
    {
        var method = GetMethod<MethodCompositeCases>(nameof(MethodCompositeCases.Case));

        var attributes = ProtoAttributeResolver.Resolve(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                attributes.Select(attribute => attribute.GetType().Name),
                Is.EqualTo(new[] { nameof(MarkerAttribute), nameof(NamedCompositeAttribute) }));
            Assert.That(attributes.OfType<MarkerAttribute>().Single().Name, Is.EqualTo("method plan-marker"));
        }
    }

    [Test]
    public void Resolve_ShouldExpandNestedCompositesRecursively()
    {
        var method = GetMethod<OuterCompositeCases>(nameof(OuterCompositeCases.Case));

        var attributes = ProtoAttributeResolver.Resolve(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                attributes.Select(attribute => attribute.GetType().Name),
                Is.EqualTo(new[]
                {
                    nameof(MarkerAttribute),
                    nameof(NamedCompositeAttribute),
                    nameof(OuterCompositeAttribute)
                }));
            Assert.That(attributes.OfType<MarkerAttribute>().Single().Name, Is.EqualTo("inner-marker"));
        }
    }

    [Test]
    public void Resolve_ShouldThrowNamingTheChain_WhenCompositesFormACycle()
    {
        var indirect = GetMethod<CycleCases>(nameof(CycleCases.Indirect));
        var self = GetMethod<CycleCases>(nameof(CycleCases.Self));

        var indirectException = Assert.Throws<InvalidOperationException>(
            () => ProtoAttributeResolver.Resolve(indirect));
        var selfException = Assert.Throws<InvalidOperationException>(
            () => ProtoAttributeResolver.Resolve(self));

        var cycleA = typeof(CycleACompositeAttribute).FullName!;
        var cycleB = typeof(CycleBCompositeAttribute).FullName!;
        var selfCycle = typeof(SelfCompositeAttribute).FullName!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(indirectException!.Message, Does.Contain($"{cycleA} → {cycleB} → {cycleA}"));
            Assert.That(selfException!.Message, Does.Contain($"{selfCycle} → {selfCycle}"));
            Assert.That(selfException.Message, Does.Contain("cycle"));
        }
    }

    [Test]
    public void Resolve_ShouldKeepOneDeclaration_WhenAnAttributeIsBothExplicitAndComposed()
    {
        var sameLevel = GetMethod<SameLevelDuplicateCases>(nameof(SameLevelDuplicateCases.Case));
        var crossLevel = GetMethod<CrossLevelDuplicateCases>(nameof(CrossLevelDuplicateCases.Case));

        var sameLevelMarkers = ProtoAttributeResolver.Resolve(sameLevel).OfType<MarkerAttribute>().ToArray();
        var crossLevelMarkers = ProtoAttributeResolver.Resolve(crossLevel).OfType<MarkerAttribute>().ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameLevelMarkers, Has.Length.EqualTo(1),
                "an attribute declared explicitly and composed by a composite runs once");
            Assert.That(crossLevelMarkers, Has.Length.EqualTo(1),
                "a method-level declaration wins over the copy a class-level composite would add");
        }
    }

    [Test]
    public void Resolve_ShouldKeepDifferentlyConfiguredDeclarations()
    {
        var method = GetMethod<DifferentConfigurationCases>(nameof(DifferentConfigurationCases.Case));

        var markers = ProtoAttributeResolver.Resolve(method).OfType<MarkerAttribute>().ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(markers, Has.Length.EqualTo(2),
                "Order is part of a declaration's identity, so a composed and an explicit marker with " +
                "different orders are two declarations");
            Assert.That(markers.Select(marker => marker.Order), Is.EqualTo(new[] { 0, 1 }));
        }
    }

    [Test]
    public void Expand_ShouldKeepMetadataAttributesAndExpandMetadataComposites()
    {
        var expanded = ProtoAttributeResolver.Expand(
            [new PlainMetadataAttribute(), new MetadataCompositeAttribute()]);

        Assert.That(
            expanded.Select(attribute => attribute.GetType().Name),
            Is.EqualTo(new[]
            {
                nameof(PlainMetadataAttribute),
                nameof(MetadataAttribute),
                nameof(MetadataCompositeAttribute)
            }),
            "a composite may group metadata attributes that hooks read, not only lifecycle attributes");
    }

    [Test]
    public void Expand_ShouldRejectNullAttributes()
    {
        Assert.Throws<ArgumentNullException>(() => ProtoAttributeResolver.Expand(null!));
    }

    [Test]
    public void Prepare_ShouldResolveTheSkipConditionOfAComposedAttribute()
    {
        var method = GetMethod<SkipCases>(nameof(SkipCases.Case));

        var preparation = ProtoTestAdapter.Prepare(method, Host);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.CanRun, Is.False);
            Assert.That(preparation.SkipReason, Is.EqualTo("the composed capability is missing"));
        }
    }

    [Test]
    public async Task Host_ShouldRunComposedAttributesAtTheirOrderAndRecordTheExpansion()
    {
        await using var host = new ProtoHostBuilder().Build();
        var method = GetMethod<ExecutionCases>(nameof(ExecutionCases.Case));

        await host.StartTestAsync(
            "composite execution", "00042", method, ProtoAttributeResolver.Resolve(method));
        var probe = Proto.Context.Resolve<ExecutionProbe>();
        probe.Steps.Add("body");
        await host.CompleteTestAsync();

        Assert.That(probe.Steps, Is.EqualTo(new[]
        {
            "alpha:before",
            "composite:before",
            "beta:before",
            "gamma:before",
            "body",
            "gamma:after",
            "beta:after",
            "composite:after",
            "alpha:after"
        }));

        var test = host.Trace.Snapshot().Tests.Single();
        var before = test.Entries.Where(entry => entry.Kind == "attribute.before").ToArray();
        var after = test.Entries.Where(entry => entry.Kind == "attribute.after").ToArray();
        var compositeStep = before.Single(
            entry => entry.Attributes["attribute.type"] == typeof(ExecutionCompositeAttribute).FullName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                before.Select(entry => entry.Attributes["attribute.type"]),
                Is.EqualTo(new[]
                {
                    typeof(AlphaExecutionAttribute).FullName,
                    typeof(ExecutionCompositeAttribute).FullName,
                    typeof(BetaExecutionAttribute).FullName,
                    typeof(GammaExecutionAttribute).FullName
                }),
                "composed attributes run at their own order, interleaved with the composite's own step");
            Assert.That(
                after.Select(entry => entry.Attributes["attribute.type"]).Reverse(),
                Is.EqualTo(before.Select(entry => entry.Attributes["attribute.type"])),
                "teardown runs the same steps in reverse");
            Assert.That(
                compositeStep.Attributes["attribute.composed"],
                Is.EqualTo(
                    $"{nameof(AlphaExecutionAttribute)}, {nameof(BetaExecutionAttribute)}"),
                "the trace names what the composite expanded to");
            Assert.That(
                test.Entries.Single(entry => entry.Kind == "test.setup").Attributes["attribute.count"],
                Is.EqualTo("4"));
        }
    }

    [Test]
    public async Task Host_ShouldRollBackCompletedComposedAttributes_WhenASetupFails()
    {
        await using var host = new ProtoHostBuilder().Build();
        var method = GetMethod<FailingCases>(nameof(FailingCases.Case));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartTestAsync(
            "composite failure", "00043", method, ProtoAttributeResolver.Resolve(method)));

        var test = host.Trace.Snapshot().Tests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("composed setup failed"));
            Assert.That(
                test.Entries
                    .Where(entry => entry.Kind == "attribute.after")
                    .Select(entry => entry.Attributes["attribute.type"]),
                Is.EqualTo(new[]
                {
                    typeof(FailingCompositeAttribute).FullName,
                    typeof(AlphaExecutionAttribute).FullName
                }),
                "rollback reverses the composed attributes that completed setup; the failing one never ran");
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
        }
    }

    private static MethodInfo GetMethod<T>(string name)
        => typeof(T).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException($"Method '{name}' was not found.");

    private static ExecutionProbe Probe(ProtoExecutionContext context)
    {
        if (context.TryResolve<ExecutionProbe>() is { } existing)
        {
            return existing;
        }

        var probe = new ExecutionProbe();
        context.SetContext(probe);
        return probe;
    }

    private sealed class ClassCompositeCases
    {
        [NamedComposite("class plan")]
        public void Case() { }
    }

    private sealed class MethodCompositeCases
    {
        [NamedComposite("method plan")]
        public void Case() { }
    }

    private sealed class OuterCompositeCases
    {
        [OuterComposite]
        public void Case() { }
    }

    private sealed class CycleCases
    {
        [CycleAComposite]
        public void Indirect() { }

        [SelfComposite]
        public void Self() { }
    }

    private sealed class SameLevelDuplicateCases
    {
        [NamedComposite("shared")]
        [Marker("shared-marker")]
        public void Case() { }
    }

    [NamedComposite("shared")]
    private sealed class CrossLevelDuplicateCases
    {
        [Marker("shared-marker")]
        public void Case() { }
    }

    private sealed class DifferentConfigurationCases
    {
        [NamedComposite("shared")]
        [Marker("shared-marker", Order = 1)]
        public void Case() { }
    }

    private sealed class SkipCases
    {
        [SkipComposite]
        public void Case() { }
    }

    [ExecutionComposite]
    private sealed class ExecutionCases
    {
        [GammaExecution]
        public void Case() { }
    }

    [FailingComposite]
    private sealed class FailingCases
    {
        public void Case() { }
    }

    private sealed class ExecutionProbe : IProtoContext
    {
        public List<string> Steps { get; } = [];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class MarkerAttribute(string name) : ProtoAttribute
    {
        public string Name { get; } = name;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class NamedCompositeAttribute(string name) : ProtoCompositeAttribute
    {
        public string Name { get; } = name;

        protected override IReadOnlyList<Attribute> Compose() =>
            [new MarkerAttribute($"{Name}-marker")];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class OuterCompositeAttribute : ProtoCompositeAttribute
    {
        protected override IReadOnlyList<Attribute> Compose() => [new NamedCompositeAttribute("inner")];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class CycleACompositeAttribute : ProtoCompositeAttribute
    {
        protected override IReadOnlyList<Attribute> Compose() => [new CycleBCompositeAttribute()];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class CycleBCompositeAttribute : ProtoCompositeAttribute
    {
        protected override IReadOnlyList<Attribute> Compose() => [new CycleACompositeAttribute()];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class SelfCompositeAttribute : ProtoCompositeAttribute
    {
        protected override IReadOnlyList<Attribute> Compose() => [new SelfCompositeAttribute()];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class SkipCompositeAttribute : ProtoCompositeAttribute
    {
        protected override IReadOnlyList<Attribute> Compose() =>
            [new RequiresCapabilityAttribute("not-composed") { Reason = "the composed capability is missing" }];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class MetadataAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class PlainMetadataAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class MetadataCompositeAttribute : ProtoCompositeAttribute
    {
        protected override IReadOnlyList<Attribute> Compose() => [new MetadataAttribute()];
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class ExecutionCompositeAttribute : ProtoCompositeAttribute
    {
        public ExecutionCompositeAttribute() => Order = -150;

        protected override IReadOnlyList<Attribute> Compose() =>
            [new AlphaExecutionAttribute(), new BetaExecutionAttribute()];

        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            Probe(context).Steps.Add("composite:before");
            return Task.CompletedTask;
        }

        public override Task AfterTestAsync(ProtoExecutionContext context)
        {
            Probe(context).Steps.Add("composite:after");
            return Task.CompletedTask;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class FailingCompositeAttribute : ProtoCompositeAttribute
    {
        public FailingCompositeAttribute() => Order = -150;

        protected override IReadOnlyList<Attribute> Compose() =>
            [new AlphaExecutionAttribute(), new FailingExecutionAttribute()];
    }

    private abstract class ExecutionAttribute : ProtoAttribute
    {
        protected ExecutionAttribute(string name, int order)
        {
            Name = name;
            Order = order;
        }

        protected ExecutionAttribute(string name)
            : this(name, 0)
        {
        }

        public string Name { get; }

        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            Probe(context).Steps.Add($"{Name}:before");
            return Task.CompletedTask;
        }

        public override Task AfterTestAsync(ProtoExecutionContext context)
        {
            Probe(context).Steps.Add($"{Name}:after");
            return Task.CompletedTask;
        }
    }

    private sealed class AlphaExecutionAttribute : ExecutionAttribute
    {
        public AlphaExecutionAttribute() : base("alpha", -200) { }
    }

    private sealed class BetaExecutionAttribute : ExecutionAttribute
    {
        public BetaExecutionAttribute() : base("beta", -100) { }
    }

    private sealed class GammaExecutionAttribute : ExecutionAttribute
    {
        public GammaExecutionAttribute() : base("gamma") { }
    }

    private sealed class FailingExecutionAttribute : ExecutionAttribute
    {
        public FailingExecutionAttribute() : base("failing") { }

        public override Task BeforeTestAsync(ProtoExecutionContext context)
            => throw new InvalidOperationException("composed setup failed");
    }
}
