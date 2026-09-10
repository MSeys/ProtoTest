namespace ProtoTest.SampleApp.Testing;

using System.Net;
using System.Net.Http.Headers;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;

public static class SampleAppTargets
{
    public const string Api = "SampleApp";
}

public sealed record SampleEnvironmentContext(
    string Tenant,
    string Name,
    Uri ApiBaseUrl) : IProtoContext;

public sealed record SampleUserContext(
    string Id,
    string Tenant,
    string Email,
    string Role,
    string AccessToken) : IProtoContext;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SampleEnvironmentAttribute : ProtoAttribute
{
    public SampleEnvironmentAttribute()
    {
        Order = -200;
    }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var environmentName = $"test-{context.TestId}";
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateEnvironmentRequest(environmentName))
            .PostAsync("/test-support/environments");

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var environment = response.ReadAsJson<EnvironmentResponse>()
            ?? throw new InvalidOperationException("The sample app returned no environment.");
        context.SetContext(new SampleEnvironmentContext(
            environment.Tenant,
            environment.Name,
            environment.ApiBaseUrl));
    }

    public override async Task AfterTestAsync(ProtoExecutionContext context)
    {
        var environment = context.TryContext<SampleEnvironmentContext>();
        if (environment is null) return;

        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .DeleteAsync("/test-support/environments/{tenant}", new { environment.Tenant });
        response.ShouldHaveStatus(HttpStatusCode.NoContent);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SampleUserAttribute : ProtoAttribute
{
    public SampleUserAttribute(string role = SampleRoles.Member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Order = -100;
    }

    public string Role { get; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var environment = context.Context<SampleEnvironmentContext>();
        var email = $"{Role}.{context.TestId}@example.test";
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateUserRequest(email, Role))
            .PostAsync("/test-support/environments/{tenant}/users", new { environment.Tenant });

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var user = response.ReadAsJson<UserResponse>()
            ?? throw new InvalidOperationException("The sample app returned no user.");
        context.SetContext(new SampleUserContext(
            user.Id,
            user.Tenant,
            user.Email,
            user.Role,
            user.AccessToken));
    }

}

public sealed class SampleUserAuthenticator : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(
        RestAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var environment = context.Test.Context<SampleEnvironmentContext>();
        var user = context.Test.Context<SampleUserContext>();
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        context.Request.Headers.Add("X-Tenant", environment.Tenant);
        return ValueTask.CompletedTask;
    }
}
