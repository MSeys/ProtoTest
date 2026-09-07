namespace ProtoTest.Core;

/// <summary>
/// Defines a contract for a component that collects and tracks coverage data during test execution.
/// </summary>
public interface IProtoCollector
{
    /// <summary>
    /// Gets the name of the target client/application being tracked.
    /// </summary>
    string TargetName { get; }

    /// <summary>
    /// Gets the domain/category of this collector (e.g., "REST", "OpenAPI", "Playwright").
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Records an incoming coverage hit. Implementations should support
    /// concurrent calls when tests or requests execute in parallel.
    /// </summary>
    void RecordHit(CoverageHit hit);

    /// <summary>
    /// Retrieves all current coverage report items tracked by this collector.
    /// </summary>
    IEnumerable<CoverageItem> GetReportItems();
}