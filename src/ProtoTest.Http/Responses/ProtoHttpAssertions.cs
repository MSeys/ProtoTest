namespace ProtoTest.Http;

using System.Net;

/// <summary>
/// The status-assertion facade an HTTP protocol exposes: <see cref="HaveHttpStatus"/> runs the
/// protocol's assertion with the facade's polarity, so <c>Should</c> and <c>ShouldNot</c> read the
/// same in every protocol.
/// </summary>
public abstract class ProtoHttpAssertions<TResponse, TAssertions>
    where TResponse : ProtoHttpResponse
    where TAssertions : ProtoHttpAssertions<TResponse, TAssertions>
{
    protected ProtoHttpAssertions(TResponse response, bool negated)
    {
        Response = response ?? throw new ArgumentNullException(nameof(response));
        Negated = negated;
    }

    /// <summary>The response the assertions run against.</summary>
    protected TResponse Response { get; }

    /// <summary>Whether the facade asks for the opposite of its assertions.</summary>
    protected bool Negated { get; }

    /// <summary>Asserts the HTTP status; the negated form asserts it is anything but <paramref name="expected"/>.</summary>
    public TResponse HaveHttpStatus(HttpStatusCode expected) => AssertStatus(expected, Negated);

    /// <summary>Runs the protocol's status assertion with the facade's polarity.</summary>
    protected abstract TResponse AssertStatus(HttpStatusCode expected, bool negated);
}
