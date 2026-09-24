namespace ProtoTest.Sheets.Tests;

using System.Globalization;
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Json;

[TestFixture]
public sealed partial class SheetsTests
{
    private static readonly DateTime ReportDate = new(2026, 9, 18);
    private readonly List<ProtoHost> _hosts = [];
    private string _path = null!;

    [TearDown]
    public async Task DisposeHostsAndDeleteWorkbook()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        _hosts.Clear();
        File.Delete(_path);
    }

    private sealed record NamedContent(
        string? MediaType,
        ReadOnlyMemory<byte> Content,
        string? FileName) : IProtoBinaryContent;

    private static ProtoReportItem[] Coverage(ProtoExecutionContext context)
        => context.Services.GetServices<IProtoCollector>()
            .OfType<SheetsCoverageCollector>()
            .Single()
            .GetReportItems()
            .ToArray();

    private (ProtoHost Host, ProtoExecutionContext Context) Start(
        string name,
        Action<SheetsOptions>? configure = null)
    {
        var builder = new ProtoHostBuilder();
        builder.AddSheets(configure);
        var host = builder.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Document), Is.True);
        // The ambient Proto.Context is an AsyncLocal that StartTestAsync sets synchronously; awaiting
        // inside an async helper would scope it to the helper and hide it from the test method.
        host.StartAsync().GetAwaiter().GetResult();
        var context = host.StartTestAsync(name, TestMethods.Placeholder).GetAwaiter().GetResult();
        _hosts.Add(host);
        return (host, context);
    }


}
