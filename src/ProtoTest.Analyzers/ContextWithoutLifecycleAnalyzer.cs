namespace ProtoTest.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Reports a read of <c>Proto.Context</c> in a method the runner registers with its own plain test
/// attribute, outside an assembly auto-wrap. The ambient context only exists inside the ProtoTest
/// lifecycle, so such a read is guaranteed to throw before the test can use it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextWithoutLifecycleAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ProtoTestDiagnostics.ContextOutsideLifecycle);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (reference.Property.Name != ProtoTestVocabulary.ContextProperty ||
            reference.Property.ContainingType.ToDisplayString() != ProtoTestVocabulary.ContextType)
        {
            return;
        }

        var method = ProtoTestMethods.EnclosingMethod(context.ContainingSymbol);
        if (method is null ||
            method.Find(ProtoTestVocabulary.LifecycleAttributes) is not null ||
            ProtoTestMethods.IsAutoWrapped(method))
        {
            return;
        }

        var plain = method.Find(ProtoTestVocabulary.PlainTestAttributes);
        if (plain is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ProtoTestDiagnostics.ContextOutsideLifecycle,
            reference.Syntax.GetLocation(),
            method.Name,
            plain.AttributeClass!.Name));
    }
}
