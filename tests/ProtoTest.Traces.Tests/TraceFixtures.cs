namespace ProtoTest.Traces.Tests;

using ProtoTest.Core;

/// <summary>Writes a small real trace so the reader and the CLI are exercised against a genuine archive.</summary>
internal static class TraceFixtures
{
    /// <summary>Writes a trace with one passing and one failing test to the given path.</summary>
    public static async Task WriteAsync(string path)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = path;
            options.EmbedSources = false;
        });
        await using var host = builder.Build();
        await host.StartAsync();

        var passing = await host.StartTestAsync("reader pass", "00070", TestMethods.Placeholder);
        using (var work = passing.Trace.Operation("reader.work", "Work", "ProtoTest.Traces.Tests").Begin())
        {
            work.Succeed();
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var failing = await host.StartTestAsync("reader fail", "00071", TestMethods.Placeholder);
        using (var check = failing.Trace.Operation("reader.check", "Check", "ProtoTest.Traces.Tests").Begin())
        {
            check.Fail(new InvalidOperationException("The check failed."));
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(new InvalidOperationException("The check failed.")));
        await host.StopAsync();
    }

    /// <summary>Runs the fixture, hands the archive to the action, and cleans the file up.</summary>
    public static async Task WithTraceAsync(Func<string, Task> act)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prototest-traces-{Guid.NewGuid():N}.prototrace");
        try
        {
            await WriteAsync(path);
            await act(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
