namespace ProtoTest.SampleApp.Contracts;

public static class MemberRoles
{
    public const string Owner = "owner";
    public const string Administrator = "admin";
    public const string Billing = "billing";
    public const string Developer = "developer";
    public const string Viewer = "viewer";

    public static bool IsSupported(string role)
        => role is Owner or Administrator or Billing or Developer or Viewer;

    public static bool CanDeployToProduction(string role) => role is Owner or Administrator;

    public static bool CanDeployToPreview(string role)
        => role is Owner or Administrator or Developer or Billing;

    public static bool CanManageBilling(string role) => role is Owner or Administrator or Billing;

    public static bool CanManageMembers(string role) => role is Owner or Administrator;

    public static bool CanManageProjects(string role) => role is Owner or Administrator;

    public static bool CanViewAudit(string role) => role is Owner or Administrator or Billing;
}

public static class MemberStatuses
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Removed = "removed";
}

public static class TokenScopes
{
    public const string Read = "read";
    public const string Deploy = "deploy";
    public const string Billing = "billing";
    public const string Admin = "admin";

    public static bool IsSupported(string scope) => scope is Read or Deploy or Billing or Admin;

    public static bool AllowsDeploy(IReadOnlyList<string> scopes)
        => scopes.Contains(Admin) || scopes.Contains(Deploy);

    public static bool AllowsBilling(IReadOnlyList<string> scopes)
        => scopes.Contains(Admin) || scopes.Contains(Billing);
}

public sealed record OrganizationResponse(
    string Id,
    string Slug,
    string Name,
    string PlanId,
    string PlanName,
    string Status,
    int SeatCount,
    int? SeatLimit,
    int ProjectCount,
    int? ProjectLimit,
    bool CancelAtPeriodEnd,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc);

public sealed record UpdateOrganizationRequest(string? Name);

public sealed record InviteMemberRequest(string Email, string Role);

public sealed record UpdateMemberRequest(string Role);

public sealed record MembershipResponse(
    string Id,
    string OrganizationId,
    string Email,
    string Role,
    string Status,
    DateTimeOffset InvitedAtUtc,
    DateTimeOffset? JoinedAtUtc);

public sealed record CreateApiTokenRequest(string Name, IReadOnlyList<string>? Scopes);

public sealed record ApiTokenResponse(
    string Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

public sealed record ApiTokenSecretResponse(ApiTokenResponse Token, string Secret);
