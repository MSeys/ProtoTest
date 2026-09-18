namespace ProtoTest.Sheets;

/// <summary>Declares which sheet of the workbook a row record models, and where its headers live.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SheetAttribute(string name) : Attribute
{
    public string Name { get; } = name;

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
}
