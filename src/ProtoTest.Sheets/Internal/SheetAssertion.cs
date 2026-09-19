namespace ProtoTest.Sheets.Internal;

using ProtoTest.Core;

/// <summary>One assertion failure: the message to throw and the attributes its failed trace carries.</summary>
internal readonly record struct SheetAssertionFailure(
    string Message,
    IReadOnlyDictionary<string, string?>? Attributes = null);

/// <summary>
/// The trace flow shared by every sheet assertion: one <c>assert.sheets</c> operation per call, opened
/// before the check runs so a thrown comparison still leaves evidence, marked succeeded or failed. The
/// caller supplies the underlying check and the failure already worded for the assertion's polarity.
/// </summary>
internal static class SheetAssertion
{
    /// <summary>Formats "Expected {subject} to {expectation}" or "Expected {subject} not to {expectation}".</summary>
    public static string Describe(string subject, string expectation, bool negated)
        => $"Expected {subject} {ProtoAssertion.Describe($"to {expectation}", negated)}";

    /// <summary>
    /// Runs one assertion: opens the operation first, evaluates the check through
    /// <see cref="ProtoAssertion.IsSatisfied(bool, bool)"/>, then succeeds it or fails it with the
    /// described failure and its extra attributes.
    /// </summary>
    public static void Run(
        ProtoExecutionContext? context,
        string title,
        IReadOnlyDictionary<string, string?> attributes,
        bool negated,
        Func<bool> holds,
        Func<SheetAssertionFailure> describeFailure)
    {
        using var operation = context?.Trace
            .Operation("assert.sheets", title, "ProtoTest.Sheets")
            .With(attributes)
            .Begin();
        bool satisfied;
        try
        {
            satisfied = ProtoAssertion.IsSatisfied(holds(), negated);
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
            throw;
        }

        if (satisfied)
        {
            operation?.Succeed();
            return;
        }

        var failure = describeFailure();
        if (failure.Attributes is { Count: > 0 })
        {
            foreach (var (name, value) in failure.Attributes)
            {
                operation?.SetAttribute(name, value);
            }
        }

        var assertion = new SpreadsheetAssertionException(failure.Message);
        operation?.Fail(assertion);
        throw assertion;
    }
}
