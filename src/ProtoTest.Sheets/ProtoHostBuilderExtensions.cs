namespace ProtoTest.Sheets;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Adds the spreadsheet capability: open generated workbooks and assert on sheets, cells and ranges.
    /// Any producer works - SpreadsheetGear, ClosedXML, EPPlus, Aspose or raw OpenXML - because the file
    /// is read as OpenXML, the format itself.
    /// </summary>
    public static IProtoHostBuilder AddSheets(
        this IProtoHostBuilder builder,
        Action<ProtoSheetsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(serviceProvider =>
            {
                var options = new ProtoSheetsOptions();
                configure?.Invoke(options);
                options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return options;
            });
            services.AddSingleton<IProtoCollector>(_ => new SheetsCoverageCollector("Sheets"));
        });
        return builder.AddCapability(new ProtoCapabilityDescriptor(
            "Sheets", ProtoCapabilityKinds.Document, "ProtoTest.Sheets"));
    }
}
