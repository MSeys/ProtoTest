namespace ProtoTest.Grpc;

using System.Globalization;
using System.Text.Json;
using global::Google.Protobuf;
using ProtoTest.Core;
using ProtoTest.Http;
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

    /// <summary>
    /// Matches one protobuf reply against an expected shape and records the assertion on the ambient
    /// <see cref="Proto.Context"/> as an <c>assert.json.shape</c> operation with a
    /// <c>grpc.contract.shape</c> observation. A mismatch fails the operation and rethrows the shape
    /// exception, so the evidence is in the trace even when the test fails.
    /// </summary>
    public static void ShouldMatchShape<TResponse>(
        this TResponse response,
        object expectedShape,
        JsonSerializerOptions? options = null)
        where TResponse : IMessage
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(expectedShape);
        var context = Proto.Context;
        ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(
                context,
                ProtoGrpcBuilder.Protocol.TraceSource,
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

    /// <summary>Asserts the status of a failed call, traced on the ambient context.</summary>
    public static global::Grpc.Core.RpcException ShouldHaveStatus(
        this global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode expected)
        => AssertStatus(exception, expected, negated: false);

    /// <summary>
    /// Asserts that a failed call does <em>not</em> carry <paramref name="unexpected"/>, traced on the
    /// ambient context.
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
        => AssertStatus(exception, unexpected, negated: true);

    private static global::Grpc.Core.RpcException AssertStatus(
        global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode expected,
        bool negated)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var actual = exception.StatusCode;
        ProtoStatusAssertion.Assert(
            Proto.Context,
            ProtoGrpcBuilder.Protocol.TraceSource,
            (int)expected,
            (int)actual,
            negated,
            () => new GrpcAssertionException(
                $"Expected gRPC status {ProtoAssertion.Describe($"{(int)expected} ({expected})", negated)}, " +
                $"but received {(int)actual} ({actual})."),
            operationKind: "assert.grpc.status",
            expectedDescription: $"{(int)expected} {expected}",
            expectedAttribute: "expected.rpc.grpc.status_code",
            actualAttribute: "actual.rpc.grpc.status_code",
            extraAttributes: new Dictionary<string, string?>
            {
                ["expected.rpc.grpc.status"] = expected.ToString(),
                ["actual.rpc.grpc.status"] = actual.ToString()
            });
        return exception;
    }
}

/// <summary>Shape-match data attached to a <c>grpc.contract.shape</c> observation.</summary>
internal sealed record GrpcShapeMatchData(Type MessageType, IReadOnlyList<string> MatchedProperties);

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

