namespace ProtoTest.SampleApp.Northstar;

using HotChocolate;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.SampleApp.Contracts;

public sealed record ApiInfo(string Name, string Version);

public sealed record PageInfo(bool HasNextPage, bool HasPreviousPage);

public sealed record ProjectConnection(IReadOnlyList<ProjectResponse> Nodes, PageInfo PageInfo, int TotalCount);

public sealed record DeploymentConnection(IReadOnlyList<DeploymentResponse> Nodes, PageInfo PageInfo, int TotalCount);

public sealed record InvoiceConnection(IReadOnlyList<InvoiceResponse> Nodes, PageInfo PageInfo, int TotalCount);

public sealed record AuditEventConnection(IReadOnlyList<AuditEventResponse> Nodes, PageInfo PageInfo, int TotalCount);

public sealed record CreateProjectInput(string Name);

public sealed record CreateEnvironmentInput(string Name, string Kind);

public sealed record CreateDeploymentInput(string Version, string CommitSha);

public sealed record ChangePlanInput(string PlanId, int? Seats);

public sealed record InviteMemberInput(string Email, string Role);

public sealed class NorthstarQuery
{
    public ApiInfo ApiInfo() => new("Northstar", "1.0");

    public OrganizationResponse Organization(
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).GetOrganization(Principal(services, accessor));

    public SubscriptionResponse Subscription(
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).GetSubscription(Principal(services, accessor));

    public IReadOnlyList<PlanResponse> Plans([Service] IServiceProvider services)
        => Store(services).GetPlans();

    public ProjectConnection Projects(
        int? first,
        string? after,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
    {
        var page = Store(services).ListProjects(Principal(services, accessor), after, first ?? 25);
        return new ProjectConnection(page.Items, Page(after, page.HasMore), page.TotalCount);
    }

    public DeploymentConnection Deployments(
        int? first,
        string? after,
        string? environmentId,
        string? projectId,
        string? status,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
    {
        var page = Store(services).ListDeployments(Principal(services, accessor), environmentId, projectId, status, after, first ?? 25);
        return new DeploymentConnection(page.Items, Page(after, page.HasMore), page.TotalCount);
    }

    public InvoiceConnection Invoices(
        int? first,
        string? after,
        string? status,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
    {
        var page = Store(services).ListInvoices(Principal(services, accessor), status, after, first ?? 25);
        return new InvoiceConnection(page.Items, Page(after, page.HasMore), page.TotalCount);
    }

    public UsageSummaryResponse UsageSummary(
        string? metric,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).GetUsageSummary(Principal(services, accessor), metric ?? UsageMetrics.DeployMinutes);

    public AuditEventConnection Audit(
        int? first,
        string? after,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
    {
        var page = Store(services).ListAudit(Principal(services, accessor), after, first ?? 25);
        return new AuditEventConnection(page.Items, Page(after, page.HasMore), page.TotalCount);
    }

    private static PageInfo Page(string? after, bool hasMore) => new(hasMore, after is not null);

    private static NorthstarStore Store(IServiceProvider services) => services.GetRequiredService<NorthstarStore>();

    internal static NorthstarPrincipal Principal(IServiceProvider services, IHttpContextAccessor accessor)
        => NorthstarGraphQLAuth.Principal(accessor);
}

public sealed class NorthstarMutation
{
    public ProjectResponse CreateProject(
        CreateProjectInput input,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).CreateProject(NorthstarQuery.Principal(services, accessor), input.Name);

    public EnvironmentResponse CreateEnvironment(
        string projectId,
        CreateEnvironmentInput input,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).CreateEnvironment(NorthstarQuery.Principal(services, accessor), projectId, input.Name, input.Kind);

    public DeploymentResponse Deploy(
        string environmentId,
        CreateDeploymentInput input,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).Deploy(NorthstarQuery.Principal(services, accessor), environmentId, input.Version, input.CommitSha);

    public DeploymentResponse RollbackDeployment(
        string deploymentId,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).RollbackDeployment(NorthstarQuery.Principal(services, accessor), deploymentId);

    public SubscriptionResponse ChangePlan(
        ChangePlanInput input,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).ChangePlan(NorthstarQuery.Principal(services, accessor), input.PlanId, input.Seats);

    public MembershipResponse InviteMember(
        InviteMemberInput input,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).InviteMember(NorthstarQuery.Principal(services, accessor), input.Email, input.Role);

    public InvoiceResponse PayInvoice(
        long invoiceId,
        string method,
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor)
        => Store(services).PayInvoice(NorthstarQuery.Principal(services, accessor), invoiceId, method);

    private static NorthstarStore Store(IServiceProvider services) => services.GetRequiredService<NorthstarStore>();
}

public sealed class NorthstarSubscription
{
    [Subscribe(With = nameof(SubscribeToDeployments))]
    public DeploymentResponse DeploymentStatusChanged([EventMessage] DeploymentResponse deployment) => deployment;

    public IAsyncEnumerable<DeploymentResponse> SubscribeToDeployments(
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
        => services.GetRequiredService<NorthstarEventBus>().Deployments(
            NorthstarQuery.Principal(services, accessor).Organization.Slug,
            cancellationToken);

    [Subscribe(With = nameof(SubscribeToInvoices))]
    public InvoiceResponse InvoicePaid([EventMessage] InvoiceResponse invoice) => invoice;

    public IAsyncEnumerable<InvoiceResponse> SubscribeToInvoices(
        [Service] IServiceProvider services,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
        => services.GetRequiredService<NorthstarEventBus>().Invoices(
            NorthstarQuery.Principal(services, accessor).Organization.Slug,
            cancellationToken);
}

internal static class NorthstarGraphQLAuth
{
    public static NorthstarPrincipal Principal(IHttpContextAccessor accessor)
    {
        try
        {
            return NorthstarHttp.Principal(accessor.HttpContext!);
        }
        catch (NorthstarException exception)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage(exception.Message)
                    .SetCode(exception.Code)
                    .Build());
        }
    }
}
