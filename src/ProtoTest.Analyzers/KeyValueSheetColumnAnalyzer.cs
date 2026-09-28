namespace ProtoTest.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Reports a <c>[Column]</c> on a property of a key-value sheet model. The model still reads the
/// label, so the mistake compiles and looks right; a key-value sheet declares its labels with
/// <c>[Label]</c>, and the analyzer is the only context-scoped way to mark the old spelling.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KeyValueSheetColumnAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ProtoTestDiagnostics.ColumnOnKeyValueSheet);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        var column = property.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == ProtoTestVocabulary.ColumnAttribute);
        if (column is null || property.ContainingType is not { } model)
        {
            return;
        }

        var sheet = model.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == ProtoTestVocabulary.SheetAttribute);
        if (sheet is null || !DeclaresKeyValue(sheet))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ProtoTestDiagnostics.ColumnOnKeyValueSheet,
            AttributeMatching.LocationOf(column, property),
            property.Name,
            model.Name));
    }

    /// <summary>Whether the [Sheet] declaration's Kind names the KeyValue member of the sheet-kind enum.</summary>
    private static bool DeclaresKeyValue(AttributeData sheet)
        => sheet.NamedArguments.Any(argument =>
            argument.Key == "Kind"
            && argument.Value.Kind == TypedConstantKind.Enum
            && argument.Value.Type?.ToDisplayString() == ProtoTestVocabulary.SheetKindType
            && argument.Value.Value is int ordinal
            && argument.Value.Type.GetMembers(ProtoTestVocabulary.SheetKindKeyValue).Any(member =>
                member is IFieldSymbol { HasConstantValue: true } field
                && field.ConstantValue is int value
                && value == ordinal));
}
