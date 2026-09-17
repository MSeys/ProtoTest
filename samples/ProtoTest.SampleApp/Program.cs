namespace ProtoTest.SampleApp;

using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Northstar;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<NorthstarEventBus>();
        builder.Services.AddSingleton<NorthstarStore>();
        builder.Services.AddSingleton<WebhookSinkRegistry>();
        // The sample runs in-process, so deliveries are routed to the sink registry. A real deployment
        // would register HttpWebhookTransport to POST to customer endpoints over the network.
        builder.Services.AddSingleton<IWebhookTransport, InProcessWebhookTransport>();
        builder.Services.AddHostedService<WebhookDispatcher>();
        builder.Services.AddHttpClient();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddGraphQLServer()
            .AddQueryType<NorthstarQuery>()
            .AddMutationType<NorthstarMutation>()
            .AddSubscriptionType<NorthstarSubscription>()
            .AddInMemorySubscriptions();

        var app = builder.Build();

        app.UseWebSockets();
        app.Use(ProblemMiddleware);
        app.UseMiddleware<IdempotencyMiddleware>();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/openapi/v1.json", () => Results.File(
            System.IO.Path.Combine(AppContext.BaseDirectory, "openapi", "northstar.v1.json"),
            "application/json"));
        app.MapNorthstarApi();
        app.MapNorthstarTestSupport();
        app.MapGraphQL("/graphql");

        app.Run();
    }

    private static async Task ProblemMiddleware(HttpContext context, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (NorthstarException exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = (int)exception.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new ProblemResponse(exception.Code, exception.Message, exception.Details));
        }
        catch (ArgumentException exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new ProblemResponse(ProblemCodes.ValidationFailed, exception.Message));
        }
    }
}
