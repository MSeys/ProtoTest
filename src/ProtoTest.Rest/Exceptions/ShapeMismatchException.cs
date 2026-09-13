namespace ProtoTest.Rest.Exceptions;

public sealed record ShapeMismatch(string PropertyPath, string Reason, object? Expected, object? Actual)
{
    public override string ToString()
        => $"  • [{PropertyPath}]: {Reason} (Expected: {Format(Expected)}, Actual: {Format(Actual)})";

    private static string Format(object? val) => val switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => $"'{val}'"
    };
}

public sealed class ShapeMismatchException : Core.ProtoAssertionException
{
    public IReadOnlyList<ShapeMismatch> Mismatches { get; }
    public IReadOnlyList<string> MatchedProperties { get; }

    public ShapeMismatchException(IReadOnlyList<ShapeMismatch> mismatches, IReadOnlyList<string>? matchedProperties = null)
        : base(BuildErrorMessage(mismatches))
    {
        Mismatches = [.. mismatches];
        MatchedProperties = [.. matchedProperties ?? []];
    }

    private static string BuildErrorMessage(IReadOnlyList<ShapeMismatch> mismatches)
    {
        ArgumentNullException.ThrowIfNull(mismatches);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Shape mismatch failed with {mismatches.Count} error(s):");
        foreach (var mismatch in mismatches)
        {
            sb.AppendLine(mismatch.ToString());
        }
        return sb.ToString().TrimEnd();
    }
}
