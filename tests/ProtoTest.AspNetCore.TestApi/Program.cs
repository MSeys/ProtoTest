namespace ProtoTest.AspNetCore.SampleApi;

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
        var app = builder.Build();
        app.MapGet("/ping", () => Results.Ok(new { Message = "pong" }));
        app.MapGet("/message", (ITestMessageService service) => Results.Ok(new { Message = service.GetMessage() }));
        app.Run();
    }
}
