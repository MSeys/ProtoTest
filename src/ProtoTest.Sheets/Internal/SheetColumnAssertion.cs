namespace ProtoTest.Sheets.Internal;

using ProtoTest.Core;

/// <summary>The first difference between two columns, with the values already projected for display.</summary>
internal readonly record struct SheetColumnMismatch(
    int Index,
    int ExpectedCount,
    int ActualCount,
    string? Expected,
    string? Actual)
{
    /// <summary>True when the columns differ in length rather than at one row.</summary>
    public bool IsCountMismatch => Index < 0;
}

/// <summary>
/// The comparison shared by text and typed model columns: values are compared through the column's own
/// comparer, and the first mismatch is projected for display so typed semantics stay separate from
/// messages.
/// </summary>
internal static class SheetColumnAssertion
{
    /// <summary>Finds the first mismatch between two value sequences, or <see langword="null"/> when equal.</summary>
    public static SheetColumnMismatch? FindMismatch<TValue>(
        IReadOnlyList<TValue?> actual,
        IReadOnlyList<TValue?> expected,
        IEqualityComparer<TValue?>? comparer,
        Func<TValue?, string?> display)
    {
        if (actual.Count != expected.Count)
        {
            return new SheetColumnMismatch(-1, expected.Count, actual.Count, null, null);
        }

        var equality = comparer ?? EqualityComparer<TValue?>.Default;
        for (var index = 0; index < actual.Count; index++)
        {
            if (!equality.Equals(actual[index], expected[index]))
            {
                return new SheetColumnMismatch(
                    index,
                    expected.Count,
                    actual.Count,
                    display(expected[index]),
                    display(actual[index]));
            }
        }

        return null;
    }
}
