namespace ProtoTest.SampleApp.Contracts;

public static class WebhookEventTypes
{
    public const string MemberInvited = "member.invited";
    public const string ProjectCreated = "project.created";
    public const string DeploymentSucceeded = "deployment.succeeded";
    public const string DeploymentFailed = "deployment.failed";
    public const string DeploymentRolledBack = "deployment.rolled_back";
    public const string PlanChanged = "plan.changed";
    public const string InvoiceIssued = "invoice.issued";
    public const string InvoicePaid = "invoice.paid";
    public const string InvoicePaymentFailed = "invoice.payment_failed";

    public static readonly IReadOnlyList<string> All =
    [
        MemberInvited,
        ProjectCreated,
        DeploymentSucceeded,
        DeploymentFailed,
        DeploymentRolledBack,
        PlanChanged,
        InvoiceIssued,
        InvoicePaid,
        InvoicePaymentFailed
    ];

    public static bool IsSupported(string type) => All.Contains(type);
}

public static class WebhookDeliveryStatuses
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
}

public sealed record CreateWebhookRequest(string Url, IReadOnlyList<string>? Events);

public sealed record WebhookEndpointResponse(
    string Id,
    string Url,
    IReadOnlyList<string> Events,
    bool Active,
    string Secret,
    DateTimeOffset CreatedAtUtc);

public sealed record WebhookDeliveryResponse(
    string Id,
    string EndpointId,
    string EventType,
    string Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeliveredAtUtc);
