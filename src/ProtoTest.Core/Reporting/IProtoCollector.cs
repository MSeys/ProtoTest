namespace ProtoTest.Core;

/// <summary>
/// Consumes observations during test execution. Reporting is optional and exposed separately through
/// <see cref="IProtoReportSource"/>.
/// </summary>
public interface IProtoCollector
{
    /// <summary>Determines whether this collector accepts an observation.</summary>
    bool CanCollect(ProtoObservation observation);

    /// <summary>
    /// Records an incoming observation. Implementations should support concurrent calls when tests
    /// or requests execute in parallel.
    /// </summary>
    void Collect(ProtoObservation observation);
}
