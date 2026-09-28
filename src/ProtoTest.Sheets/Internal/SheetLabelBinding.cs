namespace ProtoTest.Sheets.Internal;

using System.Reflection;

/// <summary>
/// One precomputed binding between a key-value model property and the row its label sits on: the
/// label text, the row, and the value rules of its <c>[Label]</c> mapping. The model resolves these
/// once when it reads the sheet.
/// </summary>
internal sealed record SheetLabelBinding(PropertyInfo Property, int Row, string Label, ISheetValueRules Rules)
{
    public string Name => Property.Name;

    public bool Optional => Rules.Optional;

    /// <summary>Whether the property can hold an empty cell: a reference type or a nullable value type.</summary>
    public bool IsNullable => SheetCellValue.IsNullable(Property.PropertyType);
}
