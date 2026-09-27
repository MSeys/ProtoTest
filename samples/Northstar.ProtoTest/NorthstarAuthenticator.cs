namespace Northstar.ProtoTest;

using System.Net.Http.Headers;
using global::ProtoTest.Http;

/// <summary>
/// Sends the signed-in member's bearer token. The member comes from the shipped <c>[SignedInAs]</c>
/// identity through <see cref="NorthstarMember.EnsureAsync"/>; shared by REST, GraphQL and gRPC via
/// <c>[Auth&lt;&gt;]</c>.
/// </summary>
public sealed class NorthstarAuthenticator : IProtoHttpAuthenticator
{
    public async ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var member = await NorthstarMember.EnsureAsync(context.Test);
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.Token);
    }
}
