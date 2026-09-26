namespace ProtoTest.Analyzers;

using Microsoft.CodeAnalysis;

/// <summary>The diagnostics the package ships. The IDs are stable; titles and messages may change.</summary>
internal static class ProtoTestDiagnostics
{
    private const string Category = "ProtoTest";

    public static readonly DiagnosticDescriptor MixedTestRegistration = new(
        id: "PT0001",
        title: "ProtoTest test attribute combined with the runner's own test attribute",
        messageFormat: "'{0}' carries both {1} and {2}. The ProtoTest attribute already derives from the plain one, so remove {2}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The ProtoTest attributes derive from the runner's plain test attributes and add the ProtoTest lifecycle, so the pair is never needed. What it does depends on the runner: NUnit resolves it to one test inside the lifecycle, MSTest fails the test with 'Only one attribute of type TestMethodAttribute is allowed' (MSTEST0060), and xUnit.net rejects multiple Fact or Theory attributes (xUnit1002). Remove the plain attribute.");

    public static readonly DiagnosticDescriptor ContextOutsideLifecycle = new(
        id: "PT0002",
        title: "ProtoTest context used in a test without the ProtoTest attribute",
        messageFormat: "'{0}' is registered by {1} and reads Proto.Context. The context only resolves inside the ProtoTest lifecycle.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A runner-registered test without a ProtoTest attribute has no ambient execution context, so Proto.Context throws before the test can use it. Register the test with the ProtoTest attribute instead of the plain runner attribute.");
}
