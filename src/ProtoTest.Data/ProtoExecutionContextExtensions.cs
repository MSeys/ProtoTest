namespace ProtoTest.Data;

using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>Gets the data capability owned by this test execution.</summary>
    public static IProtoData Data(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Service<IProtoData>();
    }
}
