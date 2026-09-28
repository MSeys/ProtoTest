namespace ProtoTest.Verification.Tests;

using ProtoTest.Core;
using ProtoTest.Reporting;

internal static class VerificationReports
{
    /// <summary>Builds a report the way the sinks build it, so the summary and the totals are real.</summary>
    public static ProtoReport Report(params ProtoReportItem[] items) => ProtoReport.Create(items);

    public static ProtoReportItem Unit(string target, string category, string identifier, bool covered)
        => new(target, category, identifier,
            Kind: ProtoReportItemKinds.Coverage,
            Status: covered ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
            Count: covered ? 1 : 0,
            IsCovered: covered);

    public static ProtoReportItem Spec(string target, string source, string hash, string category = "OpenAPI")
        => new(target, category, ProtoSpecIdentity.ReportIdentifier,
            Kind: ProtoReportItemKinds.Coverage,
            Status: ProtoReportStatus.Neutral,
            IsCovered: null,
            Metadata: new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [ProtoSpecIdentity.SourceMetadataKey] = source,
                [ProtoSpecIdentity.HashMetadataKey] = hash
            });

    public static ProtoReportItem FailedGate(string name, string message)
        => new("Run gates", "Gate", name,
            Kind: ProtoReportItemKinds.Gate,
            Status: ProtoReportStatus.Error,
            Count: 1,
            Message: message);
}
