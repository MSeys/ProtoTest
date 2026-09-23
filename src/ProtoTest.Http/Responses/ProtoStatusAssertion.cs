namespace ProtoTest.Http;

using System.Net;
using ProtoTest.Core;

/// <summary>
/// The status assertion HTTP and gRPC responses share. It records one operation and Checks section for
/// either protocol, names the expected status through the caller's description, and lets the caller
/// supply the failure its own exception type. One implementation is what keeps a status assertion
/// reading the same in every trace.
/// </summary>
public static class ProtoStatusAssertion
{
    /// <summary>
    /// Builds the failure message both status assertions share, appending the sanitized response body
    /// when there is one, so a failed status reads the same whichever protocol produced it.
    /// </summary>
    public static string DescribeFailure(
        HttpStatusCode expected,
        HttpStatusCode actual,
        bool negated,
        string? responseBody)
    {
        var expectation = ProtoAssertion.Describe($"{(int)expected} ({expected})", negated);
        var message = $"Expected HTTP status {expectation}, but received {(int)actual} ({actual}).";
        return string.IsNullOrEmpty(responseBody)
            ? message
            : $"{message}{Environment.NewLine}Response body:{Environment.NewLine}{responseBody}";
    }

    /// <summary>
    /// Asserts <paramref name="actual"/> against <paramref name="expected"/>, honoring
    /// <paramref name="negated"/>, and throws the exception <paramref name="failureFactory"/> builds
    /// when the assertion does not hold.
    /// </summary>
    /// <param name="context">The execution context to trace on; a null context asserts without tracing.</param>
    /// <param name="source">The trace source, for example <c>ProtoTest.Rest</c>.</param>
    /// <param name="expected">The expected status.</param>
    /// <param name="actual">The observed status.</param>
    /// <param name="negated">Whether the assertion asks for anything but <paramref name="expected"/>.</param>
    /// <param name="failureFactory">Builds the exception thrown when the assertion does not hold.</param>
    /// <param name="parentOperationId">The trace operation this assertion belongs under, if any.</param>
    /// <param name="requestIdentifier">The request or operation identifier recorded on the assertion.</param>
    public static void Assert(
        ProtoExecutionContext? context,
        string source,
        HttpStatusCode expected,
        HttpStatusCode actual,
        bool negated,
        Func<Exception> failureFactory,
        string? parentOperationId = null,
        string? requestIdentifier = null)
        => Assert(
            context,
            source,
            (int)expected,
            (int)actual,
            negated,
            failureFactory,
            operationKind: "assert.http.status",
            expectedDescription: $"{(int)expected} {expected}",
            expectedAttribute: "expected.status_code",
            actualAttribute: "actual.status_code",
            parentOperationId: parentOperationId,
            requestIdentifier: requestIdentifier);

    /// <summary>
    /// Asserts two integer status codes (a gRPC status code, for example) with the caller's operation
    /// kind, attribute names and expected description, so a protocol with its own status vocabulary
    /// reuses the algorithm instead of copying it.
    /// </summary>
    public static void Assert(
        ProtoExecutionContext? context,
        string source,
        int expected,
        int actual,
        bool negated,
        Func<Exception> failureFactory,
        string operationKind,
        string expectedDescription,
        string expectedAttribute,
        string actualAttribute,
        IReadOnlyDictionary<string, string?>? extraAttributes = null,
        string? parentOperationId = null,
        string? requestIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(failureFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAttribute);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualAttribute);

        var statusSatisfied = ProtoAssertion.IsSatisfied(actual == expected, negated);
        using var operation = context is null
            ? null
            : context.Trace
                .Operation(
                    operationKind,
                    $"Assert status · {ProtoAssertion.Describe(expectedDescription, negated)}",
                    source)
                .With(expectedAttribute, expected.ToString())
                .With(actualAttribute, actual.ToString())
                .With("assertion.negated", negated ? "true" : null)
                .With(extraAttributes)
                .With("request.identifier", requestIdentifier)
                .Parent(parentOperationId)
                .Begin();
        operation?.AddSection(new ProtoTraceSection(
            "Result",
            ProtoTraceSectionKind.Checks,
            [
                new(
                    "status",
                    actual.ToString(),
                    statusSatisfied
                        ? null
                        : $"expected {ProtoAssertion.Describe(expected.ToString(), negated)}",
                    statusSatisfied ? ProtoTraceSectionTone.Success : ProtoTraceSectionTone.Error)
            ]));
        try
        {
            if (!statusSatisfied)
            {
                throw failureFactory();
            }

            operation?.Succeed();
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
            throw;
        }
    }
}
