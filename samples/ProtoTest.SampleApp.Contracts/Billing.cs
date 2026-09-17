namespace ProtoTest.SampleApp.Contracts;

public static class SubscriptionStatuses
{
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";

    public static bool IsActive(string status) => status is Trialing or Active or PastDue;
}

public static class InvoiceStatuses
{
    public const string Draft = "draft";
    public const string Open = "open";
    public const string Paid = "paid";
    public const string Void = "void";
    public const string Uncollectible = "uncollectible";
}

public static class PaymentStatuses
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
}

public static class PaymentMethods
{
    public const string Visa = "pm_card_visa";
    public const string Mastercard = "pm_card_mastercard";
    public const string Declined = "pm_card_declined";
}

public sealed record ChangePlanRequest(string PlanId, int? Seats);

public sealed record SubscriptionResponse(
    string PlanId,
    string PlanName,
    string Status,
    int Seats,
    int IncludedSeats,
    decimal MonthlyBasePrice,
    decimal ExtraSeatPrice,
    long IncludedDeployMinutes,
    bool CancelAtPeriodEnd,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc);

public sealed record InvoiceLineResponse(
    string Description,
    double Quantity,
    decimal UnitPrice,
    decimal Amount);

public sealed record PaymentResponse(
    long Id,
    decimal Amount,
    string Status,
    string Method,
    string? FailureReason,
    DateTimeOffset AttemptedAtUtc);

public sealed record InvoiceResponse(
    long Id,
    string Number,
    string Status,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset DueAtUtc,
    DateTimeOffset? PaidAtUtc,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    IReadOnlyList<InvoiceLineResponse> Lines,
    IReadOnlyList<PaymentResponse> Payments);

public sealed record PayInvoiceRequest(string Method);
