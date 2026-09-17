namespace ProtoTest.Xunit;

using global::Xunit;
using global::Xunit.Sdk;

/// <summary>
/// An xUnit v2 fact that runs inside a ProtoTest execution context and records the test's real outcome.
/// Use it instead of <c>[Fact]</c>; no separate <c>[ProtoTest]</c> attribute is needed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer("ProtoTest.Xunit.Sdk.ProtoTestFactDiscoverer", "ProtoTest.Xunit")]
public class ProtoTestFactAttribute : FactAttribute;

/// <summary>
/// An xUnit v2 theory whose rows each run inside a ProtoTest execution context with their real outcome recorded.
/// Use it instead of <c>[Theory]</c>; no separate <c>[ProtoTest]</c> attribute is needed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer("ProtoTest.Xunit.Sdk.ProtoTestTheoryDiscoverer", "ProtoTest.Xunit")]
public class ProtoTestTheoryAttribute : TheoryAttribute;
