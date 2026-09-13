namespace ProtoTest.AspNetCore.SampleApi;

public interface ITestMessageService
{
    string GetMessage();
}

public sealed class TestMessageService : ITestMessageService
{
    public string GetMessage() => "Hello from AspNetCore DI!";
}

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<ITestMessageService, TestMessageService>();
        var app = builder.Build();
        app.MapGet("/ping", () => Results.Ok(new { Message = "pong" }));
        app.MapGet("/message", (ITestMessageService service) => Results.Ok(new { Message = service.GetMessage() }));
        app.Run();
    }
}
