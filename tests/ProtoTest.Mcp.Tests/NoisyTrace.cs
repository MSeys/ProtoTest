namespace ProtoTest.Mcp.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Reporting;

/// <summary>
/// Writes an oversized run at test time - one failure whose message is 200k characters and a report
/// with a thousand uncovered units - so the token-budget test proves the tool caps, not the fixture's
/// small size.
/// </summary>
internal static class NoisyTrace
{
    public static async Task WriteAsync(string directory, int uncoveredUnits, int messageCharacters)
    {
        var tracePath = Path.Combine(directory, "noisy.prototrace");
        var message = new string('x', messageCharacters);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = tracePath;
            options.EmbedSources = false;
        });
        builder.ConfigureServices(services => services.AddSingleton<IProtoReportSource>(new NoisyReportSource(uncoveredUnits)));
        builder.AddSink(new JsonReportSink
        {
            OutputPath = Path.Combine(directory, "noisy-report.json"),
            Indented = false
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var failing = await host.StartTestAsync("a noisy failure", "00007", typeof(NoisyTrace).GetMethod(nameof(NoisyFailure))!);
        using (var operation = failing.Trace
            .Operation("http.request", "POST /api/v1/noisy", "ProtoTest.Mcp.Tests")
            .With("request.identifier", "POST /api/v1/noisy")
            .Begin())
        {
            operation.Fail(new InvalidOperationException(message));
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(new InvalidOperationException(message)));
        await host.StopAsync();
    }

    public static void NoisyFailure()
    {
    }

    private sealed class NoisyReportSource(int count) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems()
            => Enumerable.Range(0, count).Select(index => new ProtoReportItem(
                "Noisy:Api",
                "OpenAPI Property",
                $"$.field{index}",
                Kind: ProtoReportItemKinds.Coverage,
                IsCovered: false));
    }
}
