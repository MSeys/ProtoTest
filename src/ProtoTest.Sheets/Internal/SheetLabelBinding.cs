namespace ProtoTest.Sheets.Internal;

using System.Reflection;

/// <summary>
/// One precomputed binding between a key-value model property and the row its label sits on: the
/// label text, the row, and the attribute that declares it. The model resolves these once when it
/// reads the sheet.
/// </summary>
internal sealed record SheetLabelBinding(PropertyInfo Property, int Row, ColumnAttribute Attribute)
{
    public string Name => Property.Name;

    public string Label => Attribute.Path[0];

    public bool Optional => Attribute.Optional;

    /// <summary>Whether the property can hold an empty cell: a reference type or a nullable value type.</summary>
    public bool IsNullable => SheetCellValue.IsNullable(Property.PropertyType);
}
