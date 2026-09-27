namespace ProtoTest.Http;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>One claim the built-in test user carries.</summary>
public sealed record ProtoTestUserClaim
{
    public ProtoTestUserClaim(string type, string value)
    {
        Type = string.IsNullOrWhiteSpace(type)
            ? throw new ArgumentException("A claim type is required.", nameof(type))
            : type;
        Value = value ?? string.Empty;
    }

    /// <summary>Gets the claim type, for example <c>tenant</c> or <see cref="System.Security.Claims.ClaimTypes.Role"/>.</summary>
    public string Type { get; init; }

    /// <summary>Gets the claim value.</summary>
    public string Value { get; init; }
}

/// <summary>
/// The identity a test acts as: a name, roles and claims. Declare one per test with
/// <c>[SignedInAs("alice", "admin")]</c> or during the test body with <c>context.SignIn(user)</c>; the
/// identity lives in the test's execution context only, so it can never leak into the next test.
/// </summary>
public sealed record ProtoTestUser : IProtoContext
{
    /// <summary>Creates an identity.</summary>
    public ProtoTestUser(
        string name,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<ProtoTestUserClaim>? claims = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A test user name is required.", nameof(name))
            : name;
        Roles = roles is null ? [] : [.. roles];
        Claims = claims is null ? [] : [.. claims];
    }

    /// <summary>Gets the user name the application sees.</summary>
    public string Name { get; init; }

    /// <summary>Gets the roles the application sees.</summary>
    public IReadOnlyList<string> Roles { get; init; }

    /// <summary>Gets the claims the application sees.</summary>
    public IReadOnlyList<ProtoTestUserClaim> Claims { get; init; }
}

/// <summary>
/// The wire form the built-in test user travels in: one <c>ProtoTest-User</c> header (a gRPC call
/// carries it as <c>prototest-user</c> metadata) holding the identity as Base64-encoded JSON. The
/// header is not a credential - it only names a test identity - and the app reads it through the
/// shipped in-process authentication; an application that does not register it ignores the header.
/// </summary>
public static class ProtoTestUserHeader
{
    /// <summary>The request header that carries the identity.</summary>
    public const string HeaderName = "ProtoTest-User";

    /// <summary>Encodes a user as the header value.</summary>
    public static string Encode(ProtoTestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var wire = new WireUser(
            user.Name,
            [.. user.Roles],
            [.. user.Claims.Select(claim => new WireClaim(claim.Type, claim.Value))]);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(wire, ProtoJsonDefaults.Web)));
    }

    /// <summary>
    /// The largest header value <see cref="TryDecode"/> accepts. The identity the header carries is
    /// small; a longer value is rejected before it is decoded, so a hostile in-process request cannot
    /// turn the header into an allocation or parse amplification path.
    /// </summary>
    internal const int MaxHeaderLength = 16 * 1024;

    /// <summary>
    /// Decodes a header value, returning false when it carries no valid test user: a blank or oversized
    /// value, a value that is not Base64, a payload that is not a Json identity, or an identity whose
    /// name, roles or claims are malformed (a null or blank role element, a null claim, or a claim
    /// without a type or value).
    /// </summary>
    public static bool TryDecode(string? headerValue, [NotNullWhen(true)] out ProtoTestUser? user)
    {
        user = null;
        if (string.IsNullOrWhiteSpace(headerValue) || headerValue.Length > MaxHeaderLength)
        {
            return false;
        }

        try
        {
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue));
            var wire = JsonSerializer.Deserialize<WireUser>(payload, ProtoJsonDefaults.Web);
            if (wire is null
                || string.IsNullOrWhiteSpace(wire.Name)
                || (wire.Roles ?? []).Any(string.IsNullOrWhiteSpace)
                || (wire.Claims ?? []).Any(claim =>
                    claim is null || string.IsNullOrWhiteSpace(claim.Type) || claim.Value is null))
            {
                return false;
            }

            user = new ProtoTestUser(
                wire.Name,
                wire.Roles ?? [],
                [.. (wire.Claims ?? []).Select(claim => new ProtoTestUserClaim(claim.Type, claim.Value))]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // A decoded payload with a blank name or claim type is not a valid identity.
            return false;
        }
    }

    private sealed record WireUser(string Name, string[]? Roles, WireClaim[]? Claims);

    private sealed record WireClaim(string Type, string Value);
}
