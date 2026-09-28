namespace ProtoTest.Analyzers;

using System.Collections.Immutable;

/// <summary>
/// The framework vocabulary the analyzers match by metadata name. An analyzer cannot reference the
/// packages it inspects - it must load into any compiler and stay valid across ProtoTest versions -
/// so the names below are the contract, and the tests pin them against the shipped attributes.
/// </summary>
internal static class ProtoTestVocabulary
{
    /// <summary>Test attributes that start the ProtoTest lifecycle in each runner adapter.</summary>
    public static readonly ImmutableHashSet<string> LifecycleAttributes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ProtoTest.MSTest.ProtoTestAttribute",
        "ProtoTest.NUnit.ProtoTestAttribute",
        "ProtoTest.Xunit.ProtoTestFactAttribute",
        "ProtoTest.Xunit.ProtoTestTheoryAttribute",
        "ProtoTest.Xunit3.ProtoTestFactAttribute",
        "ProtoTest.Xunit3.ProtoTestTheoryAttribute");

    /// <summary>
    /// The runner's own test attributes that every ProtoTest lifecycle attribute derives from. TUnit
    /// is deliberately absent: <c>ProtoTestExecutor</c> wraps every TUnit <c>[Test]</c> in the
    /// lifecycle, so its attribute is not a plain registration.
    /// </summary>
    public static readonly ImmutableHashSet<string> PlainTestAttributes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "NUnit.Framework.TestAttribute",
        "Xunit.FactAttribute",
        "Xunit.TheoryAttribute");

    /// <summary>The one static gateway the ambient execution context is read through.</summary>
    public const string ContextType = "ProtoTest.Core.Proto";

    /// <summary>The property that resolves the ambient execution context.</summary>
    public const string ContextProperty = "Context";

    /// <summary>The sheet attribute that declares a model's sheet and shape.</summary>
    public const string SheetAttribute = "ProtoTest.Sheets.SheetAttribute";

    /// <summary>The enum whose <see cref="SheetKindKeyValue"/> member marks a label/value sheet.</summary>
    public const string SheetKindType = "ProtoTest.Sheets.ProtoSheetKind";

    /// <summary>The sheet-kind member that marks a key-value model.</summary>
    public const string SheetKindKeyValue = "KeyValue";

    /// <summary>The table-mapping attribute a key-value model deprecates.</summary>
    public const string ColumnAttribute = "ProtoTest.Sheets.ColumnAttribute";
}
