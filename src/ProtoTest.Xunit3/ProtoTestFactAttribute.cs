namespace ProtoTest.Xunit3;

using ProtoTest.Core;
using Xunit;

/// <summary>
/// Marks a method as a ProtoTest Fact in xUnit v3 and manages its context lifecycle.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProtoTestFactAttribute : FactAttribute, IProtoTestXunit3Attribute
{
    ProtoTestScope? IProtoTestXunit3Attribute.Scope { get; set; }
}
