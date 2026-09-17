namespace ProtoTest.SampleApp.Contracts;

/// <summary>Stable, machine-readable error envelope returned for every failed request.</summary>
public sealed record ProblemResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);

public static class ProblemCodes
{
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string ValidationFailed = "validation_failed";
    public const string TenantMismatch = "tenant_mismatch";
    public const string PlanLimitExceeded = "plan_limit_exceeded";
    public const string PlanFeatureUnavailable = "plan_feature_unavailable";
    public const string SubscriptionInactive = "subscription_inactive";
    public const string PaymentRequired = "payment_required";
    public const string InvoiceNotPayable = "invoice_not_payable";
    public const string RateLimited = "rate_limited";
    public const string Conflict = "conflict";
}

/// <summary>Cursor page shape used by every collection endpoint.</summary>
public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore,
    int TotalCount);
