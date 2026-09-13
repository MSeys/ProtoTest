namespace ProtoTest.OpenTelemetry;

using global::OpenTelemetry.Trace;
using global::ProtoTest.Core;

public static class ProtoTestOpenTelemetryExtensions
{
    /// <summary>Adds the ProtoTest ActivitySource to an OpenTelemetry tracing pipeline.</summary>
    public static TracerProviderBuilder AddProtoTestInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(ProtoTestDiagnostics.ActivitySourceName);
    }
}
