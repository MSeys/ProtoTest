namespace ProtoTest.Xunit;

using global::Xunit;
using global::Xunit.Sdk;

/// <summary>
/// An xUnit v2 theory whose rows each run inside a ProtoTest execution context with their real outcome recorded.
/// Use it instead of <c>[Theory]</c>; no separate <c>[ProtoTest]</c> attribute is needed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer("ProtoTest.Xunit.Sdk.ProtoTestTheoryDiscoverer", "ProtoTest.Xunit")]
public class ProtoTestTheoryAttribute : TheoryAttribute;
