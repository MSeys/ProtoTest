namespace ProtoTest.SampleApp.Testing;

using System.Net;
using System.Net.Http.Headers;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.GraphQL;
using ProtoTest.SampleApp.Contracts;
using System.Text.Json;

public static class SampleAppTargets
{
    public const string Api = "SampleApp";
    public const string GraphQL = "SampleAppGraphQL";
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

public sealed record ScenarioCorrelationContext(
    string CorrelationId,
    DateTimeOffset StartedAtUtc,
    string TestName) : IProtoContext;

public sealed class ScenarioProbe
{
    private readonly List<string> _milestones = [];
    public IReadOnlyList<string> Milestones => _milestones;
    public void Mark(string milestone) => _milestones.Add(milestone);
}

public sealed class ScenarioProbeInitializer : IProtoClientInitializer<ScenarioProbe>
{
    public string Name => "ScenarioProbe";

    public Task<bool> TryInitializeAsync(
        ProtoExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.RegisterClient(new ScenarioProbe(), Name);
        return Task.FromResult(true);
    }
}

public sealed class SaasScenarioHook : IProtoTestHook
{
    public int Order => -1_000;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var correlation = new ScenarioCorrelationContext(
            $"scenario-{context.TestId}-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            context.TestName);
        context.SetContext(correlation);
        context.Client<ScenarioProbe>("ScenarioProbe").Mark("scenario-started");
        context.RecordObservation(
            "ControlPlane",
            "scenario.started",
            correlation.CorrelationId,
            new { context.TestId, context.TestName });
        context.Trace.WriteEvent(
            "saas.correlation.begin",
            "Begin correlated SaaS scenario",
            "ProtoTest.SampleApp.Testing",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["saas.correlation_id"] = correlation.CorrelationId
            });
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
    {
        var correlation = context.Context<ScenarioCorrelationContext>();
        var probe = context.Client<ScenarioProbe>("ScenarioProbe");
        probe.Mark("scenario-completed");
        var duration = DateTimeOffset.UtcNow - correlation.StartedAtUtc;
        context.RecordObservation(
            "ControlPlane",
            "scenario.completed",
            correlation.CorrelationId,
            new { duration.TotalMilliseconds, probe.Milestones });
        context.AddAttachment(
            "scenario-summary.json",
            JsonSerializer.Serialize(new
            {
                correlation.CorrelationId,
                correlation.TestName,
                DurationMs = duration.TotalMilliseconds,
                probe.Milestones
            }),
            "application/json",
            "Correlation and custom-client milestones for this SaaS scenario.");
        context.Trace.WriteEvent(
            "saas.correlation.end",
            "Complete correlated SaaS scenario",
            "ProtoTest.SampleApp.Testing",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["saas.correlation_id"] = correlation.CorrelationId,
                ["saas.duration_ms"] = duration.TotalMilliseconds.ToString("F1")
            });
        return Task.CompletedTask;
    }
}

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

public sealed class SampleGraphQLAuthenticator : IGraphQLAuthenticator
{
    public ValueTask AuthenticateAsync(
        GraphQLAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var environment = context.Test.Context<SampleEnvironmentContext>();
        var user = context.Test.Context<SampleUserContext>();
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        context.Request.Headers.Add("X-Tenant", environment.Tenant);
        return ValueTask.CompletedTask;
    }
}
