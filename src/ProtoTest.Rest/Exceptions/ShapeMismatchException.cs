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

public class ShapeMismatchException : ProtoTest.Core.ProtoAssertionException
{
    public IReadOnlyList<ShapeMismatch> Mismatches { get; }

    public ShapeMismatchException(IReadOnlyList<ShapeMismatch> mismatches)
        : base(BuildErrorMessage(mismatches))
    {
        Mismatches = mismatches;
    }

    private static string BuildErrorMessage(IReadOnlyList<ShapeMismatch> mismatches)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Shape mismatch failed with {mismatches.Count} error(s):");
        foreach (var mismatch in mismatches)
        {
            sb.AppendLine(mismatch.ToString());
        }
        return sb.ToString().TrimEnd();
    }
}
