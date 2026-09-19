namespace ProtoTest.Core;

/// <summary>
/// Runner-independent semantics shared by integrations whose assertions run in a positive or a negated
/// form. A negated assertion is satisfied when the underlying check does not hold, and its expectation
/// reads with a leading "not".
/// </summary>
public static class ProtoAssertion
{
    /// <summary>
    /// Returns whether an assertion is satisfied. A positive assertion is satisfied when
    /// <paramref name="holds"/> is true; a negated assertion is satisfied when it is false.
    /// </summary>
    /// <param name="holds">Whether the underlying check currently holds.</param>
    /// <param name="negated">Whether the assertion asks for the opposite of the check.</param>
    public static bool IsSatisfied(bool holds, bool negated = false)
        => negated ? !holds : holds;

    /// <summary>
    /// Describes an expectation the way a reader should see it, prefixing "not " for a negated
    /// assertion — for example "be visible" or "not be visible".
    /// </summary>
    /// <param name="expectation">The expectation in its positive form, such as "be visible".</param>
    /// <param name="negated">Whether the assertion asks for the opposite of the check.</param>
    public static string Describe(string expectation, bool negated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectation);
        return negated ? $"not {expectation}" : expectation;
    }
}
