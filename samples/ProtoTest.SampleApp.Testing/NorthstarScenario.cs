namespace ProtoTest.SampleApp.Testing;

using System.Text.Json;
using ProtoTest.Core;

public static class NorthstarTargets
{
    public const string Api = "Northstar";
}

/// <summary>The organization provisioned for the current test, plus its owner token.</summary>
public sealed record NorthstarOrganizationContext(
    string Tenant,
    string OrganizationId,
    string OwnerEmail,
    string OwnerToken,
    Uri ApiBaseUrl) : IProtoContext;

/// <summary>The member the test is currently acting as.</summary>
public sealed record NorthstarMemberContext(
    string Id,
    string Email,
    string Role,
    string Token) : IProtoContext;

public sealed record NorthstarScenarioContext(
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

/// <summary>
/// Stamps every test with a correlation id and a custom-client milestone trail, then publishes both as
/// an observation, an attachment and trace events.
/// </summary>
public sealed class NorthstarScenarioHook : IProtoTestHook
{
    public int Order => -1_000;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var scenario = new NorthstarScenarioContext(
            $"scenario-{context.TestId}-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            context.TestName);
        context.SetContext(scenario);
        context.Client<ScenarioProbe>("ScenarioProbe").Mark("scenario-started");
        context.RecordObservation(
            "Northstar",
            "scenario.started",
            scenario.CorrelationId,
            new { context.TestId, context.TestName });
        context.Trace.WriteEvent(
            "northstar.scenario.begin",
            "Begin correlated Northstar scenario",
            "ProtoTest.SampleApp.Testing",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["northstar.correlation_id"] = scenario.CorrelationId
            });
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
    {
        var scenario = context.Resolve<NorthstarScenarioContext>();
        var probe = context.Client<ScenarioProbe>("ScenarioProbe");
        probe.Mark("scenario-completed");
        var duration = DateTimeOffset.UtcNow - scenario.StartedAtUtc;
        context.RecordObservation(
            "Northstar",
            "scenario.completed",
            scenario.CorrelationId,
            new { duration.TotalMilliseconds, probe.Milestones });
        context.AddAttachment(
            "scenario-summary.json",
            JsonSerializer.Serialize(new
            {
                scenario.CorrelationId,
                scenario.TestName,
                DurationMs = duration.TotalMilliseconds,
                probe.Milestones
            }),
            "application/json",
            "Correlation and custom-client milestones for this Northstar scenario.");
        context.Trace.WriteEvent(
            "northstar.scenario.end",
            "Complete correlated Northstar scenario",
            "ProtoTest.SampleApp.Testing",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["northstar.correlation_id"] = scenario.CorrelationId,
                ["northstar.duration_ms"] = duration.TotalMilliseconds.ToString("F1")
            });
        return Task.CompletedTask;
    }
}
