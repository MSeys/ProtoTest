namespace ProtoTest.SampleApp;

using Microsoft.AspNetCore.Http;
using ProtoTest.SampleApp.Contracts;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<SampleSaasStore>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddGraphQLServer()
            .AddQueryType<SampleQuery>()
            .AddMutationType<SampleMutation>()
            .AddSubscriptionType<SampleSubscription>()
            .AddInMemorySubscriptions()
            .AddUploadType();

        var app = builder.Build();

        app.UseWebSockets();
        app.MapGet("/health", () => Results.Ok(new { Status = "healthy" }));
        app.MapGraphQL("/graphql");

        app.MapPost("/test-support/environments", (
            CreateEnvironmentRequest request,
            HttpRequest httpRequest,
            SampleSaasStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ErrorResponse("environment-name-required"));
            }

            var tenant = Slugify(request.Name);
            if (!store.TryCreateEnvironment(tenant, request.Name))
            {
                return Results.Conflict(new ErrorResponse("environment-already-exists"));
            }

            var baseUrl = new Uri($"{httpRequest.Scheme}://{httpRequest.Host}");
            return Results.Created(
                $"/test-support/environments/{tenant}",
                new EnvironmentResponse(tenant, request.Name, baseUrl));
        });

        app.MapDelete("/test-support/environments/{tenant}", (string tenant, SampleSaasStore store) =>
            store.DeleteEnvironment(tenant)
                ? Results.NoContent()
                : Results.NotFound(new ErrorResponse("environment-not-found")));

        app.MapPost("/test-support/environments/{tenant}/users", (
            string tenant,
            CreateUserRequest request,
            SampleSaasStore store) =>
        {
            if (!SampleRoles.IsSupported(request.Role))
            {
                return Results.BadRequest(new ErrorResponse("unsupported-role"));
            }

            var user = store.CreateUser(tenant, request.Email, request.Role);
            return user is null
                ? Results.NotFound(new ErrorResponse("environment-not-found"))
                : Results.Created($"/test-support/environments/{tenant}/users/{user.Id}", user);
        });

        app.MapGet("/api/me", (HttpRequest request, SampleSaasStore store) =>
            Authenticate(request, store, out var user, out var failure)
                ? Results.Ok(user)
                : failure!);

        app.MapPost("/api/orders", (
            CreateOrderRequest request,
            HttpRequest httpRequest,
            SampleSaasStore store) =>
        {
            if (!TryGetTenantUser(httpRequest, store, out var user, out var failure)) return failure!;
            if (request.Quantity <= 0 || request.UnitPrice <= 0 || string.IsNullOrWhiteSpace(request.Product))
            {
                return Results.BadRequest(new ErrorResponse("invalid-order"));
            }

            var order = store.CreateOrder(user!.Tenant, request.Product, request.Quantity, request.UnitPrice)!;
            return Results.Created($"/api/orders/{order.Id}", order);
        });

        app.MapGet("/api/orders/{id:int}", (
            int id,
            HttpRequest request,
            SampleSaasStore store) =>
        {
            if (!TryGetTenantUser(request, store, out var user, out var failure)) return failure!;
            var order = store.GetOrder(user!.Tenant, id);
            return order is null
                ? Results.NotFound(new ErrorResponse("order-not-found"))
                : Results.Ok(order);
        });

        app.MapGet("/api/billing/invoices", (
            string? state,
            HttpRequest request,
            SampleSaasStore store) =>
        {
            if (!TryGetTenantUser(request, store, out var user, out var failure)) return failure!;
            if (user!.Role is not (SampleRoles.BillingAdministrator or SampleRoles.TenantAdministrator))
            {
                return Results.Json(new ErrorResponse("insufficient-permissions"), statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                user.Tenant,
                State = state,
                Invoices = store.GetInvoices(user.Tenant, state)
            });
        });

        app.MapGet("/api/admin/users", (HttpRequest request, SampleSaasStore store) =>
        {
            if (!TryGetTenantUser(request, store, out var user, out var failure)) return failure!;
            if (user!.Role != SampleRoles.TenantAdministrator)
            {
                return Results.Json(new ErrorResponse("insufficient-permissions"), statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                user.Tenant,
                Users = store.GetUsers(user.Tenant)!.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
            });
        });

        app.MapPost("/api/workspaces", (
            CreateWorkspaceRequest request,
            HttpRequest httpRequest,
            SampleSaasStore store) =>
        {
            if (!TryGetTenantUser(httpRequest, store, out var user, out var failure)) return failure!;
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Region))
                return Results.BadRequest(new ErrorResponse("invalid-workspace"));
            var workspace = store.CreateWorkspace(user!.Tenant, request.Name, request.Region, request.Plan)!;
            return Results.Created($"/api/workspaces/{workspace.Id}", workspace);
        });

        app.MapGet("/api/workspaces", (HttpRequest request, SampleSaasStore store) =>
            TryGetTenantUser(request, store, out var user, out var failure)
                ? Results.Ok(store.GetWorkspaces(user!.Tenant))
                : failure!);

        app.MapPost("/api/workspaces/{workspaceId}/releases", (
            string workspaceId,
            CreateReleaseRequest request,
            HttpRequest httpRequest,
            SampleSaasStore store) =>
        {
            if (!TryGetTenantUser(httpRequest, store, out var user, out var failure)) return failure!;
            if (user!.Role != SampleRoles.TenantAdministrator)
                return Results.Json(new ErrorResponse("insufficient-permissions"), statusCode: StatusCodes.Status403Forbidden);
            var release = store.CreateRelease(user.Tenant, workspaceId, request.Version, request.CommitSha);
            return release is null
                ? Results.NotFound(new ErrorResponse("workspace-not-found"))
                : Results.Created($"/api/workspaces/{workspaceId}/releases/{release.Id}", release);
        });

        app.MapGet("/api/control-plane", (HttpRequest request, SampleSaasStore store) =>
            TryGetTenantUser(request, store, out var user, out var failure)
                ? Results.Ok(store.GetControlPlane(user!.Tenant))
                : failure!);

        app.MapGet("/api/audit", (HttpRequest request, SampleSaasStore store) =>
            TryGetTenantUser(request, store, out var user, out var failure)
                ? Results.Ok(store.GetAuditEvents(user!.Tenant))
                : failure!);

        app.Run();
    }

    private static bool TryGetTenantUser(
        HttpRequest request,
        SampleSaasStore store,
        out UserResponse? user,
        out IResult? failure)
    {
        if (!Authenticate(request, store, out user, out failure)) return false;
        if (!request.Headers.TryGetValue("X-Tenant", out var tenant)
            || !string.Equals(tenant, user!.Tenant, StringComparison.OrdinalIgnoreCase))
        {
            failure = Results.Json(new ErrorResponse("tenant-access-denied"), statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        return true;
    }

    private static bool Authenticate(
        HttpRequest request,
        SampleSaasStore store,
        out UserResponse? user,
        out IResult? failure)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        user = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? store.FindUserByToken(authorization[prefix.Length..].Trim())
            : null;
        failure = user is null
            ? Results.Json(new ErrorResponse("unauthorized"), statusCode: StatusCodes.Status401Unauthorized)
            : null;
        return user is not null;
    }

    private static string Slugify(string value)
        => string.Concat(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-')).Trim('-');
}
