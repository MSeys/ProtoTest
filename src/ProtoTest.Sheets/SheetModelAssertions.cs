namespace ProtoTest.Sheets;

using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Row-level shape assertions for record models. The row serializes with its property names and is
/// matched by the same <see cref="ProtoShapeAssertion"/> the protocol integrations use, traced on the
/// ambient <see cref="Proto.Context"/> like every other assertion that runs inside a test body.
/// </summary>
public static class SheetModelAssertions
{
    /// <summary>Matches one model row against an expected shape and traces the assertion.</summary>
    public static void ShouldMatchShape<TRow>(
        this TRow row,
        object expectedShape,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(expectedShape);
        var context = Proto.Context;
        var actual = JsonSerializer.SerializeToElement(row, options);
        try
        {
            ProtoShapeAssertion.Assert(
                new ProtoShapeAssertionContext(
                    context,
                    "ProtoTest.Sheets",
                    "Assert row shape",
                    ExtraAttributes: new Dictionary<string, string?>
                    {
                        ["sheet.row.type"] = typeof(TRow).Name
                    }),
                actual.GetRawText(),
                expectedShape,
                options);
        }
        catch (ProtoAssertionException exception)
        {
            throw new SpreadsheetAssertionException(exception.Message, exception);
        }
    }
}
