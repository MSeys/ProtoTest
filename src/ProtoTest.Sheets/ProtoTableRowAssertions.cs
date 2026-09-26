namespace ProtoTest.Sheets;

using System.Text.Json;

/// <summary>
/// The assertions of one untyped table row, reached through <see cref="ProtoTableRow.Should"/>. Shape
/// has no negated form, so the facade is positive-only. <see cref="MatchShape"/> returns the row, so
/// assertions chain.
/// </summary>
public sealed class ProtoTableRowAssertions
{
    private readonly ProtoTableRow _row;

    internal ProtoTableRowAssertions(ProtoTableRow row) => _row = row;

    /// <summary>
    /// Matches the row against an expected shape and traces the assertion. The shape is keyed by each
    /// column's leaf header name and each value is the cell's rendered value (an empty cell is null);
    /// a table whose columns share a leaf name fails instead of guessing. A mismatch is rethrown as a
    /// <see cref="SpreadsheetAssertionException"/> whose message starts with the row's
    /// <c>Sheet!Range</c>, with the matcher exception as the inner exception. Returns the row.
    /// </summary>
    public ProtoTableRow MatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        return _row.AssertShape(expectedShape, options);
    }
}
