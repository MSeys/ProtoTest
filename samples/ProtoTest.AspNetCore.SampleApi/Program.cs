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

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Service Registraties
        builder.Services.AddSingleton<ITestMessageService, TestMessageService>();

        var app = builder.Build();

        // Endpoints
        app.MapGet("/ping", () => Results.Ok(new { Message = "pong" }));

        app.MapGet("/message", (ITestMessageService service) =>
            Results.Ok(new { Message = service.GetMessage() }));

        app.Run();
    }
}