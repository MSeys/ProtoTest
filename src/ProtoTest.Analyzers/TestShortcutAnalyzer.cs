namespace ProtoTest.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Reports the shortcuts a ProtoTest test should not take: a fixed wait (<c>Task.Delay</c>,
/// <c>Thread.Sleep</c>) and a hand-made <c>HttpClient</c>. Only methods that run in the lifecycle are
/// checked; helpers, hooks and plain tests are left alone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestShortcutAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ProtoTestDiagnostics.FixedWait, ProtoTestDiagnostics.RawHttpClient);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var target = invocation.TargetMethod;
        var waits = (target.Name == "Delay" && target.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task") ||
            (target.Name == "Sleep" && target.ContainingType.ToDisplayString() == "System.Threading.Thread");
        if (!waits || IsZeroOrInfinite(invocation) || ProtoTestMethods.EnclosingMethod(context.ContainingSymbol) is not { } method ||
            !ProtoTestMethods.RunsInLifecycle(method))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ProtoTestDiagnostics.FixedWait,
            invocation.Syntax.GetLocation(),
            method.Name,
            $"{target.ContainingType.Name}.{target.Name}"));
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type?.ToDisplayString() != "System.Net.Http.HttpClient" ||
            ProtoTestMethods.EnclosingMethod(context.ContainingSymbol) is not { } method ||
            !ProtoTestMethods.RunsInLifecycle(method))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ProtoTestDiagnostics.RawHttpClient,
            creation.Syntax.GetLocation(),
            method.Name));
    }

    // Delay(0) and Sleep(0) yield, and an infinite delay waits on its token: neither is a fixed wait.
    private static bool IsZeroOrInfinite(IInvocationOperation invocation)
    {
        if (invocation.Arguments.Length == 0)
        {
            return false;
        }

        var value = invocation.Arguments[0].Value.ConstantValue;
        return value is { HasValue: true, Value: int milliseconds } && milliseconds is 0 or -1;
    }
}
