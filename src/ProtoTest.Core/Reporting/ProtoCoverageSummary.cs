namespace ProtoTest.Core;

/// <summary>Aggregated coverage for one target and category, as seen by a Run gate.</summary>
public sealed record ProtoCoverageSummary(string TargetName, string Category, int Covered, int Total)
{
    public double Ratio => Total == 0 ? 0d : (double)Covered / Total;

    public double Percentage => Ratio * 100d;
}
