namespace ProtoTest.Analyzers.Tests;

/// <summary>
/// Source stubs for the runner attribute names that cannot load next to the NUnit and xUnit v3
/// assemblies in one test compilation (MSTest injects a global using that collides with NUnit; xUnit v2
/// and v3 both define the <c>Xunit</c> attributes; TUnit has no package reference here). The analyzers
/// match attributes by metadata name, so the stubs carry the names the adapters ship.
/// </summary>
internal static class FixtureStubs
{
    public const string MSTest = """
        namespace Microsoft.VisualStudio.TestTools.UnitTesting
        {
            public sealed class TestMethodAttribute : System.Attribute { }
        }

        namespace ProtoTest.MSTest
        {
            public sealed class ProtoTestAttribute : System.Attribute { }
        }
        """;

    /// <summary>
    /// The xUnit v2 lifecycle attributes. The paired plain attributes come from the shipped xUnit
    /// assembly, whose <c>Xunit.FactAttribute</c> and <c>Xunit.TheoryAttribute</c> carry the same
    /// metadata names in v2 and v3.
    /// </summary>
    public const string XunitV2 = """
        namespace ProtoTest.Xunit
        {
            public sealed class ProtoTestFactAttribute : System.Attribute { }
            public sealed class ProtoTestTheoryAttribute : System.Attribute { }
        }
        """;

    public const string TUnit = """
        namespace TUnit.Core
        {
            public sealed class TestAttribute : System.Attribute { }
        }
        """;
}
