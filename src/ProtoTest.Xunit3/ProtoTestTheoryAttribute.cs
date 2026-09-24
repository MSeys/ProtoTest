namespace ProtoTest.Xunit3;

using ProtoTest.Core;
using Xunit;

/// <summary>
/// Marks a method as a ProtoTest Theory in xUnit v3 and manages its context lifecycle.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProtoTestTheoryAttribute : TheoryAttribute, IProtoTestXunit3Attribute;
