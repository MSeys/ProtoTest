namespace ProtoTest.SampleApp.Northstar;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;

internal static class NorthstarHttp
{
    public static NorthstarStore Store(HttpContext context)
        => context.RequestServices.GetRequiredService<NorthstarStore>();

    public static NorthstarPrincipal Principal(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var secret = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
        return Store(context).Authenticate(secret);
    }

    public static IResult Problem(NorthstarException exception) => Results.Json(
        new ProblemResponse(exception.Code, exception.Message, exception.Details),
        statusCode: (int)exception.StatusCode,
        contentType: "application/problem+json");
}
