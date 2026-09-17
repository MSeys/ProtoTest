namespace ProtoTest.SampleApp.Domain;

using ProtoTest.SampleApp.Contracts;

internal sealed record PlanDefinition(
    string Id,
    string Name,
    decimal MonthlyBasePrice,
    int IncludedSeats,
    int? MaxSeats,
    int? MaxProjects,
    long IncludedDeployMinutes,
    decimal ExtraSeatPrice,
    decimal OveragePricePerDeployMinute,
    IReadOnlyList<string> Features)
{
    public bool HasFeature(string feature) => Features.Contains(feature);
}

/// <summary>The published Northstar pricing catalogue.</summary>
internal static class NorthstarPlans
{
    private static readonly IReadOnlyList<PlanDefinition> Catalogue =
    [
        new(
            PlanIds.Free,
            "Free",
            0m,
            IncludedSeats: 3,
            MaxSeats: 3,
            MaxProjects: 1,
            IncludedDeployMinutes: 1_000,
            ExtraSeatPrice: 0m,
            OveragePricePerDeployMinute: 0m,
            Features: []),
        new(
            PlanIds.Starter,
            "Starter",
            49m,
            IncludedSeats: 5,
            MaxSeats: 5,
            MaxProjects: 3,
            IncludedDeployMinutes: 10_000,
            ExtraSeatPrice: 0m,
            OveragePricePerDeployMinute: 0.002m,
            Features: [FeatureKeys.PreviewEnvironments]),
        new(
            PlanIds.Growth,
            "Growth",
            199m,
            IncludedSeats: 10,
            MaxSeats: 100,
            MaxProjects: 10,
            IncludedDeployMinutes: 100_000,
            ExtraSeatPrice: 12m,
            OveragePricePerDeployMinute: 0.002m,
            Features: [FeatureKeys.PreviewEnvironments, FeatureKeys.AuditExport, FeatureKeys.Sso]),
        new(
            PlanIds.Enterprise,
            "Enterprise",
            499m,
            IncludedSeats: 50,
            MaxSeats: null,
            MaxProjects: null,
            IncludedDeployMinutes: 1_000_000,
            ExtraSeatPrice: 15m,
            OveragePricePerDeployMinute: 0.001m,
            Features:
            [
                FeatureKeys.PreviewEnvironments,
                FeatureKeys.AuditExport,
                FeatureKeys.Sso,
                FeatureKeys.Saml,
                FeatureKeys.DedicatedSupport
            ])
    ];

    public static IReadOnlyList<PlanDefinition> All => Catalogue;

    public static PlanDefinition Get(string planId)
        => TryGet(planId, out var plan)
            ? plan
            : throw NorthstarException.Validation($"Unknown plan '{planId}'.");

    public static bool TryGet(string planId, out PlanDefinition plan)
    {
        plan = Catalogue.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, planId, StringComparison.OrdinalIgnoreCase))!;
        return plan is not null;
    }

    public static PlanResponse ToResponse(PlanDefinition plan) => new(
        plan.Id,
        plan.Name,
        plan.MonthlyBasePrice,
        plan.IncludedSeats,
        plan.MaxSeats,
        plan.MaxProjects,
        plan.IncludedDeployMinutes,
        plan.ExtraSeatPrice,
        plan.OveragePricePerDeployMinute,
        plan.Features);
}
