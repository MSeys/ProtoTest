namespace ProtoTest.AspNetCore.SampleApi;

using Microsoft.AspNetCore.Http.Metadata;

public interface ITestMessageService
{
    string GetMessage();
}

public sealed class TestMessageService : ITestMessageService
{
    public string GetMessage() => "Hello from AspNetCore DI!";
}

public interface IScopedProbe
{
    int Instance { get; }

    bool Disposed { get; }
}

public sealed class ScopedProbe : IScopedProbe, IDisposable
{
    private static int _instances;

    public int Instance { get; } = Interlocked.Increment(ref _instances);

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<ITestMessageService, TestMessageService>();
        builder.Services.AddScoped<IScopedProbe, ScopedProbe>();
        builder.Services.AddControllers();
        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/ping", () => Results.Ok(new { Message = "pong" }));
        app.MapGet("/message", (ITestMessageService service) => Results.Ok(new { Message = service.GetMessage() }));
        app.MapGet("/redirect", () => Results.Redirect("/ping"));
        app.MapGet("/welcome", () => Results.Content("<h1>Welcome</h1>", "text/html"))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(string), ["text/html"]));
        app.MapGet("/portal/home", () => Results.Content("<h1>Portal</h1>", "text/html"))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(string), ["text/html"]));
        app.MapGet("/portal/legacy/old", () => Results.Content("<h1>Old</h1>", "text/html"))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(string), ["text/html"]));
        app.Map("/any-method", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("<h1>Any</h1>");
            })
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(string), ["text/html"]));
        app.MapGet("/api/orders", () => Results.Ok(new[] { "order-1" }));
        app.MapGet("/orders/{id}", (string id) => Results.Ok(new { Id = id }));
        app.MapGet("/cookies/set", (HttpContext context) =>
        {
            context.Response.Cookies.Append("prototest", "chocolate", new CookieOptions { Path = "/" });
            return Results.Ok();
        });
        app.MapGet("/cookies/read", (HttpContext context) =>
            Results.Text(context.Request.Cookies["prototest"] ?? "missing"));
        app.MapGet("/traceparent", (HttpContext context) =>
            Results.Text(context.Request.Headers["traceparent"].ToString()));
        if (LatePageState.Enabled)
        {
            app.MapGet("/late", () => Results.Content("<h1>Late</h1>", "text/html"))
                .WithMetadata(new ProducesResponseTypeMetadata(
                    StatusCodes.Status200OK,
                    typeof(string),
                    ["text/html"]));
        }

        app.Run();
    }
}

/// <summary>
/// Controls whether <see cref="Program"/> maps its late page endpoint, so a test can add a page after
/// an empty inventory has already run.
/// </summary>
public static class LatePageState
{
    public static bool Enabled { get; set; }
}
