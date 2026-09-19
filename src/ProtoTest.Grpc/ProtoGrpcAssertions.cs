namespace ProtoTest.Grpc;

using System.Globalization;
using System.Text.Json;
using global::Google.Protobuf;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Shape and status assertions for gRPC replies, reusing the Json shape matcher the REST and GraphQL
/// integrations use. Protobuf maps to Json with camelCase field names, so an expected anonymous shape
/// reads the same way as elsewhere.
/// </summary>
public static class ProtoGrpcAssertions
{
    private static readonly JsonFormatter Formatter = new(
        JsonFormatter.Settings.Default.WithFormatDefaultValues(true).WithFormatEnumsAsIntegers(false));

    /// <summary>Matches one protobuf reply against an expected shape.</summary>
    public static void ShouldMatchShape<TResponse>(
        this TResponse response,
        object expectedShape,
        JsonSerializerOptions? options = null)
        where TResponse : IMessage
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(expectedShape);
        _ = JsonShapeMatcher.AssertMatch(Formatter.Format(response), expectedShape, options);
    }

    /// <summary>
    /// Matches one protobuf reply against an expected shape and records the assertion on
    /// <paramref name="context"/> as an <c>assert.json.shape</c> operation with a
    /// <c>grpc.contract.shape</c> observation. A mismatch fails the operation and rethrows the shape
    /// exception, so the evidence is in the trace even when the test fails.
    /// </summary>
    public static void ShouldMatchShape<TResponse>(
        this TResponse response,
        ProtoExecutionContext context,
        object expectedShape,
        JsonSerializerOptions? options = null)
        where TResponse : IMessage
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(expectedShape);
        ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(
                context,
                "ProtoTest.Grpc",
                "Assert gRPC response shape",
                ExtraAttributes: new Dictionary<string, string?> { ["rpc.message.type"] = typeof(TResponse).FullName }),
            Formatter.Format(response),
            expectedShape,
            options,
            observation: matched => new ProtoObservation(
                typeof(TResponse).Name,
                "grpc.contract.shape",
                typeof(TResponse).FullName ?? typeof(TResponse).Name,
                new GrpcShapeMatchData(typeof(TResponse), matched)));
    }

    /// <summary>Asserts the status of a failed call, untraced.</summary>
    public static global::Grpc.Core.RpcException ShouldHaveStatus(
        this global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode expected)
        => AssertStatus(exception, expected, context: null, negated: false);

    /// <summary>
    /// Asserts the status of a failed call and records the check on <paramref name="context"/> as an
    /// <c>assert.grpc.status</c> operation with a Checks section. A mismatch fails the operation and
    /// rethrows a <see cref="GrpcAssertionException"/>; without a context the assertion is still made,
    /// untraced.
    /// </summary>
    public static global::Grpc.Core.RpcException ShouldHaveStatus(
        this global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode expected,
        ProtoExecutionContext? context = null)
        => AssertStatus(exception, expected, context, negated: false);

    /// <summary>
    /// Asserts that a failed call does <em>not</em> carry <paramref name="unexpected"/>, untraced.
    /// </summary>
    /// <remarks>
    /// Deliberate deviation: C# has no extension properties, so gRPC cannot expose the
    /// <c>Should</c>/<c>ShouldNot</c> facade REST and GraphQL use. <c>ShouldNotHaveStatus</c> delegates
    /// to the same implementation as <c>ShouldHaveStatus</c> with the polarity flipped, so the
    /// operation kind, trace name, Checks section, and failure message stay in one place.
    /// </remarks>
    public static global::Grpc.Core.RpcException ShouldNotHaveStatus(
        this global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode unexpected)
        => AssertStatus(exception, unexpected, context: null, negated: true);

    /// <summary>
    /// Asserts that a failed call does <em>not</em> carry <paramref name="unexpected"/> and records the
    /// check as an <c>assert.grpc.status</c> operation with a Checks section. A mismatch fails the
    /// operation and rethrows a <see cref="GrpcAssertionException"/>; without a context the assertion
    /// is still made, untraced.
    /// </summary>
    /// <remarks>
    /// Deliberate deviation: see <c>ShouldNotHaveStatus</c> for why gRPC has no Should facade.
    /// </remarks>
    public static global::Grpc.Core.RpcException ShouldNotHaveStatus(
        this global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode unexpected,
        ProtoExecutionContext? context = null)
        => AssertStatus(exception, unexpected, context, negated: true);

    private static global::Grpc.Core.RpcException AssertStatus(
        global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode expected,
        ProtoExecutionContext? context,
        bool negated)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var actual = exception.StatusCode;
        using var operation = context?.Trace
            .Operation(
                "assert.grpc.status",
                $"Assert status · {ProtoAssertion.Describe($"{(int)expected} {expected}", negated)}",
                "ProtoTest.Grpc")
            .With("expected.rpc.grpc.status_code", ((int)expected).ToString(CultureInfo.InvariantCulture))
            .With("expected.rpc.grpc.status", expected.ToString())
            .With("actual.rpc.grpc.status_code", ((int)actual).ToString(CultureInfo.InvariantCulture))
            .With("actual.rpc.grpc.status", actual.ToString())
            .With("assertion.negated", negated ? "true" : null)
            .Begin();
        var passed = ProtoAssertion.IsSatisfied(actual == expected, negated);
        operation?.AddSection(new ProtoTraceSection(
            "Result",
            ProtoTraceSectionKind.Checks,
            [
                new(
                    "status",
                    ((int)actual).ToString(CultureInfo.InvariantCulture),
                    passed
                        ? null
                        : $"expected {ProtoAssertion.Describe(((int)expected).ToString(CultureInfo.InvariantCulture), negated)}",
                    passed ? ProtoTraceSectionTone.Success : ProtoTraceSectionTone.Error)
            ]));

        try
        {
            if (!passed)
            {
                throw new GrpcAssertionException(
                    $"Expected gRPC status {ProtoAssertion.Describe($"{(int)expected} ({expected})", negated)}, " +
                    $"but received {(int)actual} ({actual}).");
            }
            operation?.Succeed();
            return exception;
        }
        catch (Exception failure)
        {
            operation?.Fail(failure);
            throw;
        }
    }
}

/// <summary>Shape-match data attached to a <c>grpc.contract.shape</c> observation.</summary>
public sealed record GrpcShapeMatchData(Type MessageType, IReadOnlyList<string> MatchedProperties);

/// <summary>Represents a failed gRPC assertion.</summary>
public sealed class GrpcAssertionException : ProtoAssertionException
{
    public GrpcAssertionException(string message)
        : base(message)
    {
    }

    public GrpcAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
