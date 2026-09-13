namespace ProtoTest.SampleApp.Contracts;

public static class SampleRoles
{
    public const string Member = "member";
    public const string BillingAdministrator = "billing-admin";
    public const string TenantAdministrator = "tenant-admin";

    public static bool IsSupported(string role)
        => role is Member or BillingAdministrator or TenantAdministrator;
}

public sealed record CreateEnvironmentRequest(string Name);

public sealed record EnvironmentResponse(string Tenant, string Name, Uri ApiBaseUrl);

public sealed record CreateUserRequest(string Email, string Role);

public sealed record UserResponse(string Id, string Tenant, string Email, string Role, string AccessToken);

public sealed record CreateOrderRequest(string Product, int Quantity, decimal UnitPrice);

public sealed record OrderResponse(
    int Id,
    string Tenant,
    string Product,
    int Quantity,
    decimal Total,
    string Status);

public sealed record InvoiceResponse(int Id, string Tenant, string State, decimal Total);

public sealed record CreateWorkspaceRequest(string Name, string Region, string Plan);

public sealed record WorkspaceResponse(
    string Id,
    string Tenant,
    string Name,
    string Region,
    string Plan);

public sealed record CreateReleaseRequest(string Version, string CommitSha);

public sealed record ReleaseResponse(
    string Id,
    string Tenant,
    string WorkspaceId,
    string Version,
    string CommitSha,
    string Status);

public sealed record AuditEventResponse(
    long Sequence,
    string Tenant,
    string Action,
    string Resource,
    DateTimeOffset TimestampUtc);

public sealed record ControlPlaneResponse(
    string Tenant,
    int UserCount,
    int WorkspaceCount,
    int ReleaseCount,
    int OpenInvoiceCount,
    decimal MonthlyRecurringRevenue);

public sealed record ErrorResponse(string Error);
