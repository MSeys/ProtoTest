namespace ProtoTest.Core;

using System.Diagnostics;

/// <summary>Diagnostic source information used by OpenTelemetry and other Activity listeners.</summary>
public static class ProtoTestDiagnostics
{
    public const string ActivitySourceName = "ProtoTest";
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
