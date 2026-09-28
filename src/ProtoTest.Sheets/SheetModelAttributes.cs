namespace ProtoTest.Sheets;

/// <summary>Declares which sheet of the workbook a model binds to, its shape, and where its headers live.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SheetAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>
    /// The sheet's shape: a table of rows under a header row by default, or a block of label/value
    /// pairs read with <c>KeyValueModel</c>.
    /// </summary>
    public ProtoSheetKind Kind { get; init; } = ProtoSheetKind.Table;

    /// <summary>The one or more header rows; a merged group header plus subheaders is two rows.</summary>
    public int[] HeaderRows { get; init; } = [1];
}

/// <summary>
/// Binds one key-value model property to a label, for example <c>[Label("Total")]</c>. The label must
/// appear exactly once in the sheet's first column; the value under it is read from the second column.
/// A table model declares its columns with <see cref="ColumnAttribute"/> instead.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class LabelAttribute(string label) : Attribute, ISheetValueRules
{
    /// <summary>The label text the sheet carries.</summary>
    public string Label { get; } = label;

    /// <summary>When true an empty value is fine; the property then needs a nullable type.</summary>
    public bool Optional { get; init; }

    /// <summary>Smallest allowed number; the default means no minimum.</summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>Largest allowed number; the default means no maximum.</summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>Regular expression the value's rendered text must match.</summary>
    public string? Pattern { get; init; }

    /// <summary>The only rendered values allowed.</summary>
    public string[]? OneOf { get; init; }
}

/// <summary>
/// Binds one record property to a header path, for example <c>[Column("FY26", "Amount")]</c>. A
/// key-value model declares its labels with <see cref="LabelAttribute"/> instead.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ColumnAttribute(params string[] path) : Attribute, ISheetValueRules
{
    public IReadOnlyList<string> Path { get; } = path;

    /// <summary>When true an empty cell is fine; the property then needs a nullable type.</summary>
    public bool Optional { get; init; }

    /// <summary>Smallest allowed number; the default means no minimum.</summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>Largest allowed number; the default means no maximum.</summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>Regular expression every text value must match.</summary>
    public string? Pattern { get; init; }

    /// <summary>The only text values allowed.</summary>
    public string[]? OneOf { get; init; }

    /// <summary>Every value in the column must be distinct.</summary>
    public bool Unique { get; init; }
}

/// <summary>
/// The value rules a mapping attribute carries: the rules a non-empty cell is checked against,
/// whatever the sheet's shape. Both <see cref="LabelAttribute"/> and <see cref="ColumnAttribute"/>
/// implement it, so the key-value model runs the same checks as the table model.
/// </summary>
internal interface ISheetValueRules
{
    /// <summary>When true an empty cell is fine.</summary>
    bool Optional { get; }

    /// <summary>Smallest allowed number; <see cref="double.NaN"/> means no minimum.</summary>
    double Min { get; }

    /// <summary>Largest allowed number; <see cref="double.NaN"/> means no maximum.</summary>
    double Max { get; }

    /// <summary>Regular expression every text value must match; null means no pattern.</summary>
    string? Pattern { get; }

    /// <summary>The only text values allowed; null or empty means no restriction.</summary>
    string[]? OneOf { get; }
}
