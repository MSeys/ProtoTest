namespace ProtoTest.Sheets;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        Action<SheetsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            services.TryAddSingleton(serviceProvider =>
            {
                var options = new SheetsOptions();
                configure?.Invoke(options);
                options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return options;
            });
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoCollector>(new SheetsCoverageCollector("Sheets")));
        });
        return builder.AddCapability(new ProtoCapabilityDescriptor(
            "Sheets", ProtoCapabilityKinds.Document, "ProtoTest.Sheets"));
    }
}
