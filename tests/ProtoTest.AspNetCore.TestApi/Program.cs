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
    public static void Main(string[] args) => CreateApp(args).Run();

    /// <summary>
    /// Builds the application without running it, so a suite can host it on its own loopback listener
    /// for a browser journey (<c>Program.CreateApp(["--urls", "http://127.0.0.1:0"])</c>).
    /// </summary>
    public static WebApplication CreateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<ITestMessageService, TestMessageService>();
        builder.Services.AddScoped<IScopedProbe, ScopedProbe>();
        builder.Services.AddControllers();
        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/ping", () => Results.Ok(new { Message = "pong" }));
        app.MapGet("/time", (HttpContext context) =>
            Results.Ok(new { UtcNow = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() }));
        app.UseWebSockets();
        app.Map("/ws/{deviceId}", async context =>
        {
            var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                var buffer = new byte[4096];
                while (socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }

                    var payload = buffer.AsSpan(0, result.Count).ToArray();
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                    {
                        var text = System.Text.Encoding.UTF8.GetString(payload);
                        var response = text == "BOOT" ? "BOOT_ACK" : $"{text}_ACK";
                        await socket.SendAsync(
                            System.Text.Encoding.UTF8.GetBytes(response),
                            System.Net.WebSockets.WebSocketMessageType.Text,
                            endOfMessage: true,
                            CancellationToken.None);
                    }
                    else
                    {
                        await socket.SendAsync(payload, System.Net.WebSockets.WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
                    }
                }
            }
            finally
            {
                socket.Dispose();
            }
        });
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
        app.MapPost("/benchmark/orders", (BenchmarkOrderRequest request) =>
        {
            var id = Guid.NewGuid().ToString("N");
            return Results.Created(
                $"/benchmark/orders/{id}",
                new { id, product = request.Product, quantity = request.Quantity, status = "pending" });
        });
        app.MapGet("/benchmark/orders/{id}", (string id) =>
            Results.Ok(new { id, product = "notebook", quantity = 2, status = "pending" }));
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

        return app;
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

/// <summary>
/// The create request the overhead benchmark posts: one shared shape so the raw stack and the ProtoTest
/// version of the short test send identical bodies.
/// </summary>
public sealed record BenchmarkOrderRequest(string Product, int Quantity);
