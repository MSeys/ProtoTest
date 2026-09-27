namespace ProtoTest.AspNetCore;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoTest.Http;

/// <summary>
/// Wires the built-in test user into an in-process application: the shipped handler reads the
/// <see cref="ProtoTestUserHeader.HeaderName"/> header a test's <c>[SignedInAs]</c> identity travels
/// in and authenticates the request as that user, so the application's own authorization - plain
/// <c>[Authorize]</c>, role checks, policies - sees the test user as its principal. The scheme becomes
/// the application's default, replacing whatever the application configured: register it only where a
/// test user should stand in for the application's own authentication, never when the test's subject is
/// that authentication itself. The header only exists while the application runs in-process.
/// </summary>
public static class TestUserAuthenticationExtensions
{
    /// <summary>The authentication scheme the app-side test-user handler registers.</summary>
    public const string Scheme = "ProtoTest.TestUser";

    /// <summary>
    /// Adds the app-side test-user authentication inside an <c>AddAspNetCoreServer</c> web-host callback:
    /// <c>app.AddAspNetCoreServer&lt;Program&gt;(webHost =&gt; webHost.AddTestUserAuthentication())</c>.
    /// </summary>
    public static IWebHostBuilder AddTestUserAuthentication(this IWebHostBuilder webHost)
    {
        ArgumentNullException.ThrowIfNull(webHost);
        return webHost.ConfigureTestServices(services =>
            services.AddAuthentication(Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestUserAuthenticationHandler>(Scheme, configureOptions: null));
    }
}

/// <summary>
/// Authenticates a request from the shipped test-user header; no header means no result, so anonymous
/// requests stay anonymous and the application's authorization challenges them as usual. A malformed
/// or oversized header fails authentication instead of throwing, so an invalid identity stays
/// anonymous rather than failing the request.
/// </summary>
internal sealed class TestUserAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ProtoTestUserHeader.HeaderName, out var values) || values.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!ProtoTestUserHeader.TryDecode(values[0], out var user))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"The '{ProtoTestUserHeader.HeaderName}' header does not carry a valid ProtoTest test user."));
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, user.Name) };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(user.Claims.Select(claim => new Claim(claim.Type, claim.Value)));
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, TestUserAuthenticationExtensions.Scheme, ClaimTypes.Name, ClaimTypes.Role));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, TestUserAuthenticationExtensions.Scheme)));
    }
}
