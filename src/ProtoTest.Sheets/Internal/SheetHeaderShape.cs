namespace ProtoTest.Sheets.Internal;

/// <summary>
/// The one comparison between a model's declared column paths and the header paths a sheet carries.
/// A declared path matches when the actual path ends with it, the same suffix rule the table lookup
/// uses; the comparison is positional, so the model's declaration order is the sheet's column order.
/// </summary>
internal static class SheetHeaderShape
{
    /// <summary>Describes the first place the sheet's headers diverge; null when every column matches in order.</summary>
    public static string? Difference(
        IReadOnlyList<IReadOnlyList<string>> declared,
        IReadOnlyList<IReadOnlyList<string>> actual)
    {
        if (declared.Count != actual.Count)
        {
            return $"it has {actual.Count} columns and the model declares {declared.Count}";
        }

        for (var index = 0; index < declared.Count; index++)
        {
            if (!Matches(actual[index], declared[index]))
            {
                return $"column {index + 1} is '{Join(actual[index])}' where the model declares '{Join(declared[index])}'";
            }
        }

        return null;
    }

    /// <summary>Formats the paths for evidence: segments joined with " / ", columns with ", ".</summary>
    public static string Describe(IReadOnlyList<IReadOnlyList<string>> headers)
        => string.Join(", ", headers.Select(Join));

    private static string Join(IReadOnlyList<string> path)
        => path.Count == 0 ? "<empty>" : string.Join(" / ", path);

    private static bool Matches(IReadOnlyList<string> actual, IReadOnlyList<string> declared)
        => declared.Count <= actual.Count
            && actual.Skip(actual.Count - declared.Count)
                .Zip(declared, (header, wanted) => string.Equals(header, wanted, StringComparison.Ordinal))
                .All(match => match);
}
