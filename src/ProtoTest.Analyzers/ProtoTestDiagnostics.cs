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

    public static readonly DiagnosticDescriptor FixedWait = new(
        id: "PT0003",
        title: "ProtoTest test waits a fixed time",
        messageFormat: "'{0}' waits a fixed time with {1}. Move time with Proto.Context.Clock, or wait for the condition with ProtoPolling.PollAsync.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A fixed wait is either too short on a slow machine, which makes the test flaky, or longer than needed everywhere else. A ProtoTest test moves time with the test clock, or polls for the condition it waits on with a deadline, and the trace records both.");

    public static readonly DiagnosticDescriptor RawHttpClient = new(
        id: "PT0004",
        title: "ProtoTest test creates its own HttpClient",
        messageFormat: "'{0}' creates an HttpClient. Call the application through Proto.Context.Rest() so the call is traced, asserted and counted toward coverage.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A client the test creates itself bypasses the host's composed client: it does not reach an in-process application, records no trace operation and counts toward no coverage. Use the client the host registered.");
}
