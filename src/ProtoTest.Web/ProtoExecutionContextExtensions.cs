namespace ProtoTest.Web;

using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    public static WebSession Web(this ProtoExecutionContext context, string name = "Default")
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Client<WebSession>(name);
    }
}
