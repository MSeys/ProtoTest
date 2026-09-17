namespace ProtoTest.SampleApp.Contracts;

public static class PlanIds
{
    public const string Free = "free";
    public const string Starter = "starter";
    public const string Growth = "growth";
    public const string Enterprise = "enterprise";
}

public static class FeatureKeys
{
    public const string PreviewEnvironments = "preview_environments";
    public const string AuditExport = "audit_export";
    public const string Sso = "sso";
    public const string Saml = "saml";
    public const string DedicatedSupport = "dedicated_support";
}

public sealed record PlanResponse(
    string Id,
    string Name,
    decimal MonthlyBasePrice,
    int IncludedSeats,
    int? MaxSeats,
    int? MaxProjects,
    long IncludedDeployMinutes,
    decimal ExtraSeatPrice,
    decimal OveragePricePerDeployMinute,
    IReadOnlyList<string> Features);
