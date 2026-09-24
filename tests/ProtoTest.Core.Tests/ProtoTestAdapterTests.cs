namespace ProtoTest.Core.Tests;

using System.Reflection;

[TestFixture]
public sealed class ProtoTestAdapterTests
{
    private static readonly ProtoHost Host = new ProtoHostBuilder().Build();

    [OneTimeTearDown]
    public static async Task DisposeHost() => await Host.DisposeAsync();

    [Test]
    public void Prepare_ShouldResolveAttributesAndDefaultTestName()
    {
        var method = GetMethod<Cases>(nameof(Cases.Plain));

        var preparation = ProtoTestAdapter.Prepare(method, Host);

        Assert.Multiple(() =>
        {
            Assert.That(preparation.Method, Is.SameAs(method));
            Assert.That(preparation.TestName, Is.EqualTo(ProtoTestName.FromMethod(method)));
            Assert.That(preparation.CanRun, Is.True);
            Assert.That(preparation.SkipReason, Is.Null);
            Assert.That(preparation.Attributes.OfType<MarkerAttribute>().Select(attribute => attribute.Name),
                Is.EqualTo(new[] { "Class", "Method" }));
        });
    }

    [Test]
    public void Prepare_ShouldKeepTheAdaptersDisplayName()
    {
        var method = GetMethod<Cases>(nameof(Cases.Plain));

        var preparation = ProtoTestAdapter.Prepare(method, Host, "Plain(value: 2)");

        Assert.That(preparation.TestName, Is.EqualTo("Plain(value: 2)"));
    }

    [Test]
    public void Prepare_ShouldResolveTheSkipReason()
    {
        var method = GetMethod<Cases>(nameof(Cases.Skipped));

        var preparation = ProtoTestAdapter.Prepare(method, Host);

        Assert.Multiple(() =>
        {
            Assert.That(preparation.CanRun, Is.False);
            Assert.That(preparation.SkipReason, Is.EqualTo("not composed with the test capability"));
        });
    }

    [Test]
    public void Prepare_ShouldRejectNullArguments()
    {
        var method = GetMethod<Cases>(nameof(Cases.Plain));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => ProtoTestAdapter.Prepare(null!, Host));
            Assert.Throws<ArgumentNullException>(() => ProtoTestAdapter.Prepare(method, null!));
        });
    }

    private static MethodInfo GetMethod<T>(string name)
        => typeof(T).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException($"Method '{name}' was not found.");

    [Marker("Class")]
    private sealed class Cases
    {
        [Marker("Method")]
        public void Plain() { }

        [RequiresCapability("not-composed", Reason = "not composed with the test capability")]
        public void Skipped() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class MarkerAttribute(string name) : ProtoAttribute
    {
        public string Name { get; } = name;
    }
}



