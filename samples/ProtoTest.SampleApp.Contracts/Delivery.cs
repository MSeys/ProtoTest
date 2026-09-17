namespace ProtoTest.SampleApp.Contracts;

public static class ProjectStatuses
{
    public const string Active = "active";
    public const string Archived = "archived";
}

public static class EnvironmentKinds
{
    public const string Preview = "preview";
    public const string Production = "production";

    public static bool IsSupported(string kind) => kind is Preview or Production;
}

public static class EnvironmentStatuses
{
    public const string Active = "active";
    public const string Paused = "paused";
}

public static class DeploymentStatuses
{
    public const string Queued = "queued";
    public const string Building = "building";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string RolledBack = "rolled_back";
}

public static class UsageMetrics
{
    public const string DeployMinutes = "deploy_minutes";
    public const string StorageGb = "storage_gb";
    public const string BandwidthGb = "bandwidth_gb";
}

public sealed record CreateProjectRequest(string Name);

public sealed record ProjectResponse(
    string Id,
    string Name,
    string Slug,
    string Status,
    int EnvironmentCount,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateEnvironmentRequest(string Name, string Kind);

public sealed record EnvironmentResponse(
    string Id,
    string ProjectId,
    string Name,
    string Kind,
    string Status,
    string? CurrentVersion,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateDeploymentRequest(string Version, string CommitSha);

public sealed record DeploymentResponse(
    string Id,
    string ProjectId,
    string EnvironmentId,
    string Version,
    string CommitSha,
    string Status,
    string RequestedBy,
    double DeployMinutes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record UsageRecordResponse(
    long Id,
    string Metric,
    double Quantity,
    DateTimeOffset OccurredAtUtc);

public sealed record RecordUsageRequest(string Metric, double Quantity);

public sealed record UsageSummaryResponse(
    string Metric,
    double Total,
    long Included,
    double Overage,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);
