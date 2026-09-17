namespace ProtoTest.SampleApp.Contracts;

/// <summary>Provisions an isolated organization (a "tenant") with an owner and an API token.</summary>
public sealed record ProvisionTenantRequest(string Name, string PlanId = PlanIds.Free);

public sealed record TenantResponse(
    string Tenant,
    string OrganizationId,
    string OwnerEmail,
    string OwnerToken,
    string ApiToken,
    Uri ApiBaseUrl);

public sealed record TestMemberResponse(MembershipResponse Membership, string Token);

public sealed record WebhookSinkResponse(string Id, Uri Url);

public sealed record AdvanceClockRequest(int Days = 0, int Hours = 0, int Minutes = 0);

public sealed record ClockResponse(string Tenant, DateTimeOffset NowUtc);

/// <summary>Configures how the in-app webhook sink responds to deliveries.</summary>
public sealed record ConfigureWebhookSinkRequest(string? SinkId = null, int FailuresBeforeSuccess = 0);

public sealed record WebhookReceiptResponse(
    string Id,
    string SinkId,
    string EventType,
    string Signature,
    string Body,
    DateTimeOffset ReceivedAtUtc);
