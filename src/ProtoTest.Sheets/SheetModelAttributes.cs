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

/// <summary>Binds one record property to a header path, for example <c>[Column("FY26", "Amount")]</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ColumnAttribute(params string[] path) : Attribute
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
