namespace ProtoTest.Sheets;

using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Row-level shape assertions for record models and table rows. A row serializes with its property
/// names (record models) or its leaf header names (table rows) and is matched by the same
/// <see cref="ProtoShapeAssertion"/> the protocol integrations use. A model row traces on the ambient
/// <see cref="Proto.Context"/>; a table row traces on the context its table carries, so a row asserted
/// from a helper flow that does not see the ambient context still lands under the owning test.
/// </summary>
public static class SheetModelAssertions
{
    /// <summary>
    /// Matches one model row against an expected shape and traces the assertion. Returns the row.
    /// </summary>
    /// <remarks>
    /// A model row is a user record, so C# cannot give it a <c>Should</c> extension property; this
    /// extension is the documented generic-subject exception. A failure names the record type.
    /// </remarks>
    public static TRow ShouldMatchShape<TRow>(
        this TRow row,
        object expectedShape,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(expectedShape);
        var actual = JsonSerializer.SerializeToElement(row, options);
        AssertShape(actual, typeof(TRow).Name, typeof(TRow).Name, expectedShape, options, Proto.Context);
        return row;
    }

    /// <summary>
    /// Matches one table row against an expected shape and traces the assertion. A table row has no
    /// record to serialize, so the shape is keyed by each column's leaf header name and each value is
    /// the cell's rendered value (an empty cell is null); a table whose columns share a leaf name
    /// fails instead of guessing. Returns the row.
    /// </summary>
    /// <remarks>Obsolete: use <c>row.Should.MatchShape(shape)</c>.</remarks>
    [Obsolete("Use row.Should.MatchShape(shape) instead.")]
    public static ProtoTableRow ShouldMatchShape(
        this ProtoTableRow row,
        object expectedShape,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Should.MatchShape(expectedShape, options);
    }

    /// <summary>
    /// The one traced shape assertion behind the model-row extension and
    /// <see cref="ProtoTableRowAssertions.MatchShape"/>. The caller supplies the context the
    /// assertion belongs to: the ambient one for a model row, the owning table's for a table row. A
    /// matcher failure is rethrown as a <see cref="SpreadsheetAssertionException"/> whose message
    /// starts with the row's subject, with the matcher exception - and its mismatch list - as the
    /// inner exception.
    /// </summary>
    internal static void AssertShape(
        JsonElement actual,
        string rowType,
        string subject,
        object expectedShape,
        JsonSerializerOptions? options,
        ProtoExecutionContext? context)
    {
        try
        {
            ProtoShapeAssertion.Assert(
                new ProtoShapeAssertionContext(
                    context,
                    ProtoSheets.TraceSource,
                    "Assert row shape",
                    ExtraAttributes: new Dictionary<string, string?> { ["sheet.row.type"] = rowType }),
                actual.GetRawText(),
                expectedShape,
                options);
        }
        catch (ProtoAssertionException exception)
        {
            throw new SpreadsheetAssertionException($"{subject} — {exception.Message}", exception);
        }
    }
}
