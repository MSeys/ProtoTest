namespace ProtoTest.Sheets;

/// <summary>The shape a modelled sheet has: a table of header rows, or a block of label/value pairs.</summary>
public enum ProtoSheetKind
{
    /// <summary>Row records bound to columns under a header row (the default).</summary>
    Table = 0,

    /// <summary>One value per label: labels in the sheet's first column, values in the second.</summary>
    KeyValue = 1
}
