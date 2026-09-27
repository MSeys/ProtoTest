namespace ProtoTest.WireMock;

using ProtoTest.Core;

/// <summary>
/// Aggregates one fake's observations as stub coverage: every registered stub is an item, uncovered
/// until a matched request for it arrives. A stub the run registered but no test's system under test
/// called stays visible as a gap, like an unhit route in REST coverage.
/// </summary>
public sealed class WireMockCoverageCollector(string targetName)
    : ProtoCoverageCollector(targetName, kind: null)
{
    public override string Category => ProtoWireMockProtocol.CoverageCategory;

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation)
            && (string.Equals(observation.Kind, ProtoWireMockProtocol.ResponseObservationKind, StringComparison.Ordinal)
                || string.Equals(observation.Kind, ProtoWireMockProtocol.StubObservationKind, StringComparison.Ordinal));

    public override void Collect(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (string.Equals(observation.Kind, ProtoWireMockProtocol.StubObservationKind, StringComparison.Ordinal))
        {
            lock (_lock)
            {
                // A stub registration opens the gap; a matched request closes it. Never downgrade: a
                // stub one test hit stays covered when another test registers it again.
                if (!_items.ContainsKey(observation.Identifier))
                {
                    _items[observation.Identifier] = new ProtoReportItem(
                        TargetName,
                        Category,
                        observation.Identifier,
                        Kind: ProtoReportItemKinds.Coverage,
                        Status: ProtoReportStatus.Neutral,
                        IsCovered: false);
                }
            }

            return;
        }

        base.Collect(observation);
    }
}
