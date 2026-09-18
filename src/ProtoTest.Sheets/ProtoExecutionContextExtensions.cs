namespace ProtoTest.Sheets;

using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>Entry point for opening generated workbooks and asserting on them.</summary>
    public static ProtoSheets Sheets(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = context.TryService<ProtoSheetsOptions>()
            ?? throw new InvalidOperationException(
                "Sheets is not composed for this host. Call AddSheets on the host builder.");
        return new ProtoSheets(context, options);
    }
}
