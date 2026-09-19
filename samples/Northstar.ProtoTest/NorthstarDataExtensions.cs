namespace Northstar.ProtoTest;

using global::ProtoTest.Data;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>An environment fixture: the application's own request plus the project it belongs to.</summary>
public sealed record ProvisionEnvironmentRequest(string ProjectId, string Name, string Kind);

/// <summary>A deployment fixture: the application's own request plus the environment it lands in.</summary>
public sealed record ProvisionDeploymentRequest(string EnvironmentId, string Version, string CommitSha);

/// <summary>Closes a billing period: usage, then enough clock to issue the next invoice.</summary>
public sealed record IssueInvoiceRequest(
    string Metric = UsageMetrics.DeployMinutes,
    double Quantity = 1,
    int Days = 31);

/// <summary>Readable shortcuts over the Northstar data provisioners, so journeys arrange in one line.</summary>
public static class NorthstarDataExtensions
{
    /// <summary>Provisions a project: through the domain when the store is composed, otherwise the API.</summary>
    public static ValueTask<ProjectResponse> CreateProjectAsync(this IProtoData data, string name)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.For<CreateProjectRequest>()
            .With(request => request.Name, name)
            .CreateAsync<ProjectResponse>();
    }

    /// <summary>Provisions an environment under a project.</summary>
    public static ValueTask<EnvironmentResponse> CreateEnvironmentAsync(
        this IProtoData data,
        string projectId,
        string name,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.For<ProvisionEnvironmentRequest>()
            .With(request => request.ProjectId, projectId)
            .With(request => request.Name, name)
            .With(request => request.Kind, kind)
            .CreateAsync<EnvironmentResponse>();
    }

    /// <summary>Deploys a version to an environment.</summary>
    public static ValueTask<DeploymentResponse> DeployAsync(
        this IProtoData data,
        string environmentId,
        string version,
        string commitSha)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.For<ProvisionDeploymentRequest>()
            .With(request => request.EnvironmentId, environmentId)
            .With(request => request.Version, version)
            .With(request => request.CommitSha, commitSha)
            .CreateAsync<DeploymentResponse>();
    }

    /// <summary>Records usage, advances the tenant clock past the period end, and returns the open invoice.</summary>
    public static ValueTask<InvoiceResponse> IssueInvoiceAsync(
        this IProtoData data,
        string metric = UsageMetrics.DeployMinutes,
        double quantity = 1,
        int days = 31)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.For<IssueInvoiceRequest>()
            .With(request => request.Metric, metric)
            .With(request => request.Quantity, quantity)
            .With(request => request.Days, days)
            .CreateAsync<InvoiceResponse>();
    }

    /// <summary>Advances the tenant's virtual clock by whole days.</summary>
    public static ValueTask<ClockResponse> AdvanceClockAsync(this IProtoData data, int days)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.For<AdvanceClockRequest>()
            .With(request => request.Days, days)
            .CreateAsync<ClockResponse>();
    }

    /// <summary>
    /// Provisions a member with the given role and returns the token the test can act with, the same
    /// way <c>[SignedInAs]</c> does. The member default keeps the email unique per fixture.
    /// </summary>
    public static async ValueTask<string> CreateMemberTokenAsync(this IProtoData data, string role)
    {
        ArgumentNullException.ThrowIfNull(data);
        var member = await data.For<InviteMemberRequest>()
            .With(request => request.Role, role)
            .CreateAsync<TestMemberResponse>();
        return member.Token;
    }
}
