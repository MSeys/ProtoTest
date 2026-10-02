namespace ProtoTest.OpenApi.TestApi;

/// <summary>An application that documents itself with Swashbuckle, for the OpenAPI coverage tests.</summary>
public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();
        app.UseSwagger();
        app.MapGet("/users/{id}", (string id) => Results.Ok(new { id, name = "Ada" }));
        app.Run();
    }
}
