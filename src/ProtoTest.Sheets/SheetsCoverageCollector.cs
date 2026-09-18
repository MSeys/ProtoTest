namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>Aggregates the ranges a test read as sheet coverage: one item per <c>Sheet!A1:C10</c>.</summary>
public sealed class SheetsCoverageCollector(string targetName) : ProtoCoverageCollector(targetName)
{
    public override string Category => "Sheets";

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation)
           && string.Equals(observation.Kind, "sheets.range", StringComparison.Ordinal);
}
