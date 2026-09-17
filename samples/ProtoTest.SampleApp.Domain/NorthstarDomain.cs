namespace ProtoTest.SampleApp.Domain;

using ProtoTest.SampleApp.Contracts;

internal sealed class Organization
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public string PlanId { get; set; } = PlanIds.Free;
    public string Status { get; set; } = OrganizationStatuses.Active;
    public bool CancelAtPeriodEnd { get; set; }
    public TimeSpan ClockOffset { get; set; }
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public DateTimeOffset? CanceledAtUtc { get; set; }
    public decimal CreditBalance { get; set; }
    public long InvoiceSequence { get; set; }

    public List<Membership> Members { get; } = [];
    public List<ApiToken> Tokens { get; } = [];
    public List<Project> Projects { get; } = [];
    public List<UsageRecord> Usage { get; } = [];
    public List<Invoice> Invoices { get; } = [];
    public List<WebhookEndpoint> Webhooks { get; } = [];
    public List<WebhookDelivery> Deliveries { get; } = [];
    public List<AuditEvent> Audit { get; } = [];

    public int BillableSeats => Members.Count(member => member.Status != MemberStatuses.Removed);

    public PlanDefinition Plan => NorthstarPlans.Get(PlanId);
}

internal static class OrganizationStatuses
{
    public const string Active = "active";
    public const string Suspended = "suspended";
}

internal sealed class Membership
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string Email { get; init; }
    public string Role { get; set; } = MemberRoles.Viewer;
    public string Status { get; set; } = MemberStatuses.Invited;
    public required DateTimeOffset InvitedAtUtc { get; init; }
    public DateTimeOffset? JoinedAtUtc { get; set; }
    public string? Token { get; set; }
}

internal sealed class ApiToken
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Prefix { get; init; }
    public required string Secret { get; init; }
    public required List<string> Scopes { get; init; }
    public required string MemberId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public List<DateTimeOffset> RequestTimestamps { get; set; } = [];
}

internal sealed class Project
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Slug { get; init; }
    public string Status { get; set; } = ProjectStatuses.Active;
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public List<Environment> Environments { get; } = [];
}

internal sealed class Environment
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string Status { get; set; } = EnvironmentStatuses.Active;
    public string? CurrentVersion { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public List<Deployment> Deployments { get; } = [];
}

internal sealed class Deployment
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string EnvironmentId { get; init; }
    public required string Version { get; init; }
    public required string CommitSha { get; init; }
    public required string Status { get; set; }
    public required string RequestedBy { get; init; }
    public required double DeployMinutes { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

internal sealed class UsageRecord
{
    public long Id { get; set; }
    public required string Metric { get; init; }
    public required double Quantity { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

internal sealed class Invoice
{
    public long Id { get; set; }
    public required string Number { get; init; }
    public string Status { get; set; } = InvoiceStatuses.Open;
    public required DateTimeOffset PeriodStartUtc { get; init; }
    public required DateTimeOffset PeriodEndUtc { get; init; }
    public required DateTimeOffset IssuedAtUtc { get; init; }
    public required DateTimeOffset DueAtUtc { get; init; }
    public DateTimeOffset? PaidAtUtc { get; set; }
    public required List<InvoiceLine> Lines { get; init; }
    public List<Payment> Payments { get; } = [];

    public decimal Subtotal => Lines.Sum(line => line.Amount);
    public decimal Tax => Math.Round(Subtotal * 0.20m, 2, MidpointRounding.AwayFromZero);
    public decimal Total => Subtotal + Tax;
}

internal sealed record InvoiceLine(string Description, double Quantity, decimal UnitPrice, decimal Amount);

internal sealed class Payment
{
    public long Id { get; set; }
    public required decimal Amount { get; init; }
    public required string Status { get; init; }
    public required string Method { get; init; }
    public required string? FailureReason { get; init; }
    public required DateTimeOffset AttemptedAtUtc { get; init; }
}

internal sealed class WebhookEndpoint
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public required List<string> Events { get; init; }
    public required string Secret { get; init; }
    public bool Active { get; set; } = true;
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

internal sealed class WebhookDelivery
{
    public required string Id { get; init; }
    public required string EndpointId { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public string Status { get; set; } = WebhookDeliveryStatuses.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
}

internal sealed class AuditEvent
{
    public long Sequence { get; set; }
    public required string Action { get; init; }
    public required string Resource { get; init; }
    public required string Actor { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required List<AuditMetadataEntry> Metadata { get; init; }
}

/// <summary>The authenticated caller: an organization, the acting member, and the token's scopes.</summary>
internal sealed record NorthstarPrincipal(Organization Organization, Membership Member, ApiToken Token)
{
    public IReadOnlyList<string> Scopes => Token.Scopes;

    public string Actor => Member.Email;

    public bool HasScope(string scope) => Scopes.Contains(TokenScopes.Admin) || Scopes.Contains(scope);
}
