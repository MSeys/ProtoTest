namespace ProtoTest.Sheets.Internal;

using System.Reflection;

/// <summary>
/// One precomputed binding between a record property and its sheet column: the column number, the
/// attribute that declares it, and the type facts the read and verify paths need. The model resolves
/// these once when it reads the sheet instead of re-reading attributes per row.
/// </summary>
internal sealed record SheetColumnBinding(PropertyInfo Property, int Number, ColumnAttribute Attribute)
{
    public string Name => Property.Name;

    public bool Optional => Attribute.Optional;

    /// <summary>Whether the property can hold an empty cell: a reference type or a nullable value type.</summary>
    public bool IsNullable => SheetCellValue.IsNullable(Property.PropertyType);
}
