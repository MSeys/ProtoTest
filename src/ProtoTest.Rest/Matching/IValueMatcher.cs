namespace ProtoTest.Rest.Matching;

public interface IValueMatcher
{
    /// <summary>
    /// Human-readable constraint included in diagnostics and test attachments.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Validates whether the actual value matches the expected constraint.
    /// </summary>
    /// <param name="actual">The value extracted from the JSON response.</param>
    /// <param name="errorMessage">Detailed reason if the match fails.</param>
    bool Matches(object? actual, out string? errorMessage);
}
