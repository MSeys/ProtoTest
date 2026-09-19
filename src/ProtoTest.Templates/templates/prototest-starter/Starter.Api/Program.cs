using Starter.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<OrderStore>();

var app = builder.Build();

app.MapPost("/api/orders", (NewOrder order, OrderStore orders) =>
{
    if (string.IsNullOrWhiteSpace(order.Product))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["A product is required."] });
    if (order.Quantity < 1)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["quantity"] = ["Order at least one."] });

    var created = orders.Add(order);
    return Results.Created($"/api/orders/{created.Id}", created);
});

app.MapGet("/api/orders/{id:int}", (int id, OrderStore orders) =>
    orders.Find(id) is { } order ? Results.Ok(order) : Results.NotFound());

app.Run();

// Makes the entry point visible to the test project, which hosts this application in-process.
public partial class Program;
