namespace ProtoTest.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Reports a method that carries both a ProtoTest lifecycle attribute and the runner's own plain test
/// attribute. The ProtoTest attribute already derives from the plain one and adds the lifecycle, so
/// one registration is enough; what the pair does differs per runner.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MixedTestRegistrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ProtoTestDiagnostics.MixedTestRegistration);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        var lifecycle = method.Find(ProtoTestVocabulary.LifecycleAttributes);
        if (lifecycle is null)
        {
            return;
        }

        var plain = method.Find(ProtoTestVocabulary.PlainTestAttributes);
        if (plain is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ProtoTestDiagnostics.MixedTestRegistration,
            AttributeMatching.LocationOf(plain, method),
            method.Name,
            lifecycle.AttributeClass!.Name,
            plain.AttributeClass!.Name));
    }
}
