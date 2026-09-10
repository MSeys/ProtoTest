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

public sealed record ErrorResponse(string Error);
