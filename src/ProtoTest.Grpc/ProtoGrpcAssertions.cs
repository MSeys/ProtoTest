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
    /// <summary>The protobuf-to-JSON formatter shared by the reply shape assertions.</summary>
    internal static readonly JsonFormatter Formatter = new(
        JsonFormatter.Settings.Default.WithFormatDefaultValues(true).WithFormatEnumsAsIntegers(false));

    /// <summary>
    /// The assertion facade of one reply message: <c>ProtoGrpcAssertions.For(reply).Should.MatchShape(shape)</c>.
    /// C# has no extension properties, so a message reaches its <c>Should</c> through this factory.
    /// </summary>
    public static ProtoGrpcMessageAssertions<TResponse> For<TResponse>(TResponse response)
        where TResponse : IMessage
    {
        ArgumentNullException.ThrowIfNull(response);
        return new ProtoGrpcMessageAssertions<TResponse>(response);
    }

    /// <summary>
    /// Matches one protobuf reply against an expected shape and records the assertion on the ambient
    /// <see cref="Proto.Context"/> as an <c>assert.json.shape</c> operation with a
    /// <c>grpc.contract.shape</c> observation. A mismatch fails the operation and rethrows the shape
    /// exception, so the evidence is in the trace even when the test fails. Returns the message, so
    /// shape assertions chain.
    /// </summary>
    /// <remarks>Obsolete: use <c>ProtoGrpcAssertions.For(response).Should.MatchShape(shape)</c>.</remarks>
    [Obsolete("Use ProtoGrpcAssertions.For(response).Should.MatchShape(shape) instead.")]
    public static TResponse ShouldMatchShape<TResponse>(
        this TResponse response,
        object expectedShape,
        JsonSerializerOptions? options = null)
        where TResponse : IMessage
        => For(response).Should.MatchShape(expectedShape, options);

    /// <summary>
    /// The assertion facade for a failed call: <c>ProtoGrpcAssertions.For(exception).Should.HaveStatus(...)</c>,
    /// or <c>.ShouldNot</c> for the negated form. The extension methods remain only because C# has no
    /// extension properties.
    /// </summary>
    public static ProtoGrpcExceptionAssertions For(global::Grpc.Core.RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ProtoGrpcExceptionAssertions(exception, negated: false);
    }

    /// <summary>Asserts the status of a failed call, traced on the ambient context.</summary>
    /// <remarks>Obsolete: use <c>ProtoGrpcAssertions.For(exception).Should.HaveStatus(expected)</c>.</remarks>
    [Obsolete("Use ProtoGrpcAssertions.For(exception).Should.HaveStatus(expected) instead.")]
    public static global::Grpc.Core.RpcException ShouldHaveStatus(
        this global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode expected)
        => For(exception).Should.HaveStatus(expected);

    /// <summary>Asserts that a failed call does <em>not</em> carry <paramref name="unexpected"/>.</summary>
    /// <remarks>Obsolete: use <c>ProtoGrpcAssertions.For(exception).ShouldNot.HaveStatus(unexpected)</c>.</remarks>
    [Obsolete("Use ProtoGrpcAssertions.For(exception).ShouldNot.HaveStatus(unexpected) instead.")]
    public static global::Grpc.Core.RpcException ShouldNotHaveStatus(
        this global::Grpc.Core.RpcException exception,
        global::Grpc.Core.StatusCode unexpected)
        => For(exception).ShouldNot.HaveStatus(unexpected);
}

/// <summary>
/// The <c>Should</c>/<c>ShouldNot</c> facade of a failed gRPC call, reached through
/// <see cref="ProtoGrpcAssertions.For"/>. Every assertion returns the exception it asserted on.
/// </summary>
public sealed class ProtoGrpcExceptionAssertions
{
    private readonly global::Grpc.Core.RpcException _exception;
    private readonly bool _negated;

    internal ProtoGrpcExceptionAssertions(global::Grpc.Core.RpcException exception, bool negated)
    {
        _exception = exception;
        _negated = negated;
    }

    /// <summary>The positive assertions of the failed call, for example <c>Should.HaveStatus(NotFound)</c>.</summary>
    public ProtoGrpcExceptionAssertions Should => new(_exception, negated: false);

    /// <summary>The negated assertions of the failed call, for example <c>ShouldNot.HaveStatus(Internal)</c>.</summary>
    public ProtoGrpcExceptionAssertions ShouldNot => new(_exception, negated: true);

    /// <summary>
    /// Asserts the call's status; the negated form asserts it is anything but
    /// <paramref name="expected"/>. Returns the exception.
    /// </summary>
    public global::Grpc.Core.RpcException HaveStatus(global::Grpc.Core.StatusCode expected)
    {
        var actual = _exception.StatusCode;
        ProtoStatusAssertion.Assert(
            Proto.Context,
            ProtoGrpcBuilder.Protocol.TraceSource,
            (int)expected,
            (int)actual,
            _negated,
            () => new GrpcAssertionException(
                $"Expected gRPC status {ProtoAssertion.Describe($"{(int)expected} ({expected})", _negated)}, " +
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
        return _exception;
    }
}

/// <summary>
/// The shape-assertion facade of one gRPC reply message, reached through
/// <see cref="ProtoGrpcAssertions.For{TResponse}(TResponse)"/>. Shape has no negated form, so the
/// facade is positive-only. <see cref="MatchShape"/> returns the message, so assertions chain.
/// </summary>
public sealed class ProtoGrpcMessageAssertions<TResponse>
    where TResponse : IMessage
{
    private readonly TResponse _response;

    internal ProtoGrpcMessageAssertions(TResponse response) => _response = response;

    /// <summary>The positive assertions of the reply, for example <c>Should.MatchShape(shape)</c>.</summary>
    public ProtoGrpcMessageAssertions<TResponse> Should => this;

    /// <summary>
    /// Matches the reply against an expected shape through the shared matcher and records the
    /// assertion on the ambient <see cref="Proto.Context"/>. A mismatch is rethrown as a
    /// <see cref="GrpcAssertionException"/> whose message starts with the message type, with the
    /// matcher exception - and its mismatch list - as the inner exception. Returns the message.
    /// </summary>
    public TResponse MatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        var context = Proto.Context;
        var messageType = typeof(TResponse);
        var subject = messageType.FullName ?? messageType.Name;
        try
        {
            ProtoShapeAssertion.Assert(
                new ProtoShapeAssertionContext(
                    context,
                    ProtoGrpcBuilder.Protocol.TraceSource,
                    "Assert gRPC response shape",
                    ExtraAttributes: new Dictionary<string, string?> { ["rpc.message.type"] = messageType.FullName }),
                ProtoGrpcAssertions.Formatter.Format(_response),
                expectedShape,
                options,
                observation: matched => new ProtoObservation(
                    messageType.Name,
                    ProtoGrpcBuilder.ShapeObservationKind,
                    subject,
                    new GrpcShapeMatchData(messageType, matched)));
        }
        catch (JsonShapeMismatchException exception)
        {
            throw new GrpcAssertionException($"{subject} — {exception.Message}", exception);
        }
        catch (JsonDocumentAssertionException exception)
        {
            throw new GrpcAssertionException($"{subject} — {exception.Message}", exception);
        }

        return _response;
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

