namespace ProtoTest.Data;

using ProtoTest.Core;

/// <summary>Exposes the active test context to a data provisioner.</summary>
public sealed class ProtoDataProvisioningContext
{
    internal ProtoDataProvisioningContext(ProtoExecutionContext execution)
    {
        Execution = execution;
    }

    public ProtoExecutionContext Execution { get; }
    public IServiceProvider Services => Execution.Services;
    public string TestId => Execution.TestId;
    public IProtoTraceWriter Trace => Execution.Trace;
}
