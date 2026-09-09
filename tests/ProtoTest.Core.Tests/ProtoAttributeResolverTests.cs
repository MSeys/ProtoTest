namespace ProtoTest.Core.Tests;

using NUnit.Framework;
using System.Reflection;

[TestFixture]
public sealed class ProtoAttributeResolverTests
{
    [Test]
    public void Resolve_ShouldReturnClassAttributesBeforeMethodAttributes()
    {
        var method = GetMethod<DerivedCases>(nameof(DerivedCases.Combined));

        var attributes = ProtoAttributeResolver.Resolve(method).Cast<MarkerAttribute>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(attributes.Take(2).Select(attribute => attribute.Name),
                Is.EquivalentTo(new[] { "BaseClass", "DerivedClass" }));
            Assert.That(attributes.Skip(2).Select(attribute => attribute.Name),
                Is.EquivalentTo(new[] { "MethodOne", "MethodTwo" }));
        });
    }

    [Test]
    public void Resolve_ShouldIncludeInheritedClassAndMethodAttributes()
    {
        var method = GetMethod<DerivedCases>(nameof(DerivedCases.InheritedMethod));

        var attributes = ProtoAttributeResolver.Resolve(method).Cast<MarkerAttribute>().ToArray();

        Assert.That(attributes.Select(attribute => attribute.Name),
            Does.Contain("BaseClass").And.Contain("InheritedMethod"));
    }

    [Test]
    public void Resolve_ShouldReturnAnEmptyList_WhenNoAttributesApply()
    {
        var method = GetMethod<NoAttributeCases>(nameof(NoAttributeCases.None));

        Assert.That(ProtoAttributeResolver.Resolve(method), Is.Empty);
    }

    [Test]
    public void Resolve_ShouldRejectNullMethod()
    {
        Assert.Throws<ArgumentNullException>(() => ProtoAttributeResolver.Resolve(null!));
    }

    private static MethodInfo GetMethod<T>(string name)
        => typeof(T).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException($"Method '{name}' was not found.");

    [Marker("BaseClass")]
    private abstract class BaseCases
    {
        [Marker("InheritedMethod")]
        public virtual void InheritedMethod() { }
    }

    [Marker("DerivedClass")]
    private sealed class DerivedCases : BaseCases
    {
        [Marker("MethodOne")]
        [Marker("MethodTwo")]
        public void Combined() { }

        public override void InheritedMethod() { }
    }

    private sealed class NoAttributeCases
    {
        public void None() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class MarkerAttribute(string name) : ProtoAttribute
    {
        public string Name { get; } = name;
    }
}
