namespace ProtoTest.AspNetCore.SampleApi;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// This is a sample ASP.NET Core application demonstrating dependency injection and minimal APIs.
/// Note: This is not a demo, but used for demos itself.
/// </summary>
public interface ITestMessageService
{
    string GetMessage();
}

public class TestMessageService : ITestMessageService
{
    public string GetMessage() => "Hello from AspNetCore DI!";
}

public interface IScenarioIdProvider
{
    Guid Id { get; }
}

public sealed class ScenarioIdProvider : IScenarioIdProvider
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed record CreateOrderRequest(string Product, int Quantity);

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<ITestMessageService, TestMessageService>();
        builder.Services.AddScoped<IScenarioIdProvider, ScenarioIdProvider>();

        var app = builder.Build();

        app.MapGet("/ping", () => Results.Ok(new { Message = "pong" }));

        app.MapGet("/message", (ITestMessageService service) =>
            Results.Ok(new { Message = service.GetMessage() }));

        app.MapPost("/orders", (CreateOrderRequest request) =>
        {
            if (request.Quantity <= 0)
            {
                return Results.BadRequest(new { Error = "quantity-must-be-positive" });
            }

            return Results.Created("/orders/101", new
            {
                Id = 101,
                request.Product,
                request.Quantity,
                Status = "pending"
            });
        });

        app.Run();
    }
}
