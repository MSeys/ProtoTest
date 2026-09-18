namespace ProtoTest.SampleApp.Domain;

using System.Diagnostics;

/// <summary>
/// The domain's own instrumentation: ordinary .NET activities, no test tooling. A host that collects
/// telemetry - an observability pipeline or a ProtoTest run that watches the source - can consume them.
/// </summary>
internal static class NorthstarDiagnostics
{
    public static readonly ActivitySource Source = new("Northstar.Domain", "1.0");
}
