namespace Northstar.ProtoTest;

using System.Net.Http.Headers;
using global::ProtoTest.Http;

/// <summary>Sends the signed-in member's bearer token. Shared by REST and GraphQL via [Auth&lt;&gt;].</summary>
public sealed class NorthstarAuthenticator : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var member = context.Test.Resolve<NorthstarMemberContext>();
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.Token);
        return ValueTask.CompletedTask;
    }
}
