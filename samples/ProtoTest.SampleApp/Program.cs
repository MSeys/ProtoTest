namespace ProtoTest.SampleApp;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Northstar;
using System.Data.Common;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // The database is infrastructure: by default an in-memory SQLite database kept alive for the
        // application's lifetime, or whatever ConnectionStrings:Northstar points at. The same schema
        // and the same store either way.
        var connectionString = builder.Configuration.GetConnectionString("Northstar");
        SqliteConnection? sharedConnection = null;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            sharedConnection = new SqliteConnection("Data Source=:memory:");
            sharedConnection.Open();
            builder.Services.AddSingleton<DbConnection>(sharedConnection);
        }

        builder.Services.AddNorthstarDomain(options =>
        {
            if (sharedConnection is not null)
            {
                options.UseSqlite(sharedConnection);
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

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

        // A real deployment migrates; the sample creates whatever is missing so a fresh in-memory
        // database is ready on startup.
        using (var scope = app.Services.CreateScope())
        {
            using var database = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<NorthstarDbContext>>()
                .CreateDbContext();
            database.Database.EnsureCreated();
        }

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
