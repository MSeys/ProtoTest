var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "aspire-test-service");

app.Run();
