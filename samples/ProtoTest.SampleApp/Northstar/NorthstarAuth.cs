namespace ProtoTest.SampleApp.Northstar;

/// <summary>
/// One place for how a browser or a client proves who it is: an <c>Authorization: Bearer</c> header for
/// API clients, or the cookie the sign-in page exchanges a token for. The console reuses both.
/// </summary>
internal static class NorthstarAuth
{
    public const string TokenCookie = "northstar_token";

    public static string? Token(HttpContext context)
        => BearerToken(context) ?? CookieToken(context);

    public static string? BearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    public static string? CookieToken(HttpContext context) => context.Request.Cookies[TokenCookie];

    public static void SetCookie(HttpContext context, string token) => context.Response.Cookies.Append(
        TokenCookie,
        token,
        new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax
        });

    public static void ClearCookie(HttpContext context) => context.Response.Cookies.Delete(TokenCookie);
}
