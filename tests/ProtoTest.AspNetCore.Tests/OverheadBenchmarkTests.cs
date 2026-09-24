namespace ProtoTest.AspNetCore.Tests;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// Plan 4 W6 / feature-plan A7: the published overhead comparison. The headline is a short but real test -
/// POST an order, read it back, assert both responses - written once with ProtoTest (start, REST calls,
/// complete, with the trace written at the end of the run) and once with the raw WebApplicationFactory and
/// System.Net.Http.Json. A micro comparison (single request, lifecycle without a request, tracing on and
/// off) explains where the difference comes from, and each side's suite startup is measured through its
/// first completed request. The numbers feed the benchmarks page; the assertions are generous sanity
/// bounds, not performance targets.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Benchmark")]
public sealed class OverheadBenchmarkTests
{
    private const int Warmup = 32;
    private const int Iterations = 256;
    private const int StartupRounds = 5;

    private static readonly ProtoAttribute[] ApiAttributes =
        [new ApplicationAttribute("Api", "Rest:Api")];

    [Test]
    public async Task ShortTestComparison_ShouldStayWithinTheSanityBound()
    {
        var tracing = await MeasureProtoAsync("short-test-prototest-tracing", tracing: true, ShortTestAsync);
        var withoutTracing = await MeasureProtoAsync("short-test-prototest-no-tracing", tracing: false, ShortTestAsync);
        var raw = await MeasureRawAsync("short-test-raw-webapplicationfactory", ShortTestRawAsync);

        Assert.Multiple(() =>
        {
            Assert.That(tracing.MedianMs, Is.LessThan(25), "the ProtoTest short test stays far below 25 ms");
            Assert.That(withoutTracing.MedianMs, Is.LessThan(25), "tracing off stays far below 25 ms");
            Assert.That(raw.MedianMs, Is.LessThan(25), "the raw short test stays far below 25 ms");
        });
    }

    [Test]
    public async Task PerRequestOverhead_ShouldStayWithinTheSanityBound()
    {
        var lifecycle = await MeasureProtoAsync("prototest-lifecycle-only", tracing: false, exercise: null);
        var tracing = await MeasureProtoAsync("prototest-tracing", tracing: true, PingAsync);
        var withoutTracing = await MeasureProtoAsync("prototest-no-tracing", tracing: false, PingAsync);
        var raw = await MeasureRawAsync("raw-webapplicationfactory", PingRawAsync);

        Assert.Multiple(() =>
        {
            Assert.That(lifecycle.MedianMs, Is.LessThan(25), "the bare lifecycle stays far below 25 ms");
            Assert.That(tracing.MedianMs, Is.LessThan(25), "a ProtoTest request stays far below 25 ms");
            Assert.That(withoutTracing.MedianMs, Is.LessThan(25), "tracing off stays far below 25 ms");
            Assert.That(raw.MedianMs, Is.LessThan(25), "the raw baseline stays far below 25 ms");
        });
    }

    [Test]
    public async Task SuiteStartup_ShouldStayWithinTheSanityBound()
    {
        var tracing = await MeasureStartupAsync(tracing: true);
        var withoutTracing = await MeasureStartupAsync(tracing: false);
        var raw = await MeasureRawStartupAsync();

        TestContext.Progress.WriteLine(
            "[overhead] startup mode=prototest-tracing median=" + F(tracing, 0) + " ms");
        TestContext.Progress.WriteLine(
            "[overhead] startup mode=prototest-no-tracing median=" + F(withoutTracing, 0) + " ms");
        TestContext.Progress.WriteLine(
            "[overhead] startup mode=raw-webapplicationfactory median=" + F(raw, 0) + " ms");

        Assert.Multiple(() =>
        {
            Assert.That(tracing, Is.LessThan(5000), "suite startup stays far below 5 s");
            Assert.That(withoutTracing, Is.LessThan(5000), "suite startup with tracing off stays far below 5 s");
            Assert.That(raw, Is.LessThan(5000), "the raw baseline startup stays far below 5 s");
        });
    }

    /// <summary>
    /// The short test, raw side: create a client, POST an order, read its id from the response, GET it
    /// back and check the body - the same work through the same in-process server.
    /// </summary>
    private static async Task ShortTestRawAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync(
            "/benchmark/orders",
            new { product = "notebook", quantity = 2 });
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"The raw create failed: {created.StatusCode}.");
        }

        var createdJson = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync());
        var id = createdJson.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("The raw create returned no id.");
        if (createdJson.GetProperty("product").GetString() != "notebook" ||
            createdJson.GetProperty("quantity").GetInt32() != 2 ||
            createdJson.GetProperty("status").GetString() != "pending")
        {
            throw new InvalidOperationException("The raw create returned an unexpected body.");
        }

        using var read = await client.GetAsync($"/benchmark/orders/{id}");
        read.EnsureSuccessStatusCode();
        var readJson = JsonSerializer.Deserialize<JsonElement>(await read.Content.ReadAsStringAsync());
        if (readJson.GetProperty("id").GetString() != id ||
            readJson.GetProperty("product").GetString() != "notebook" ||
            readJson.GetProperty("quantity").GetInt32() != 2 ||
            readJson.GetProperty("status").GetString() != "pending")
        {
            throw new InvalidOperationException("The raw read returned an unexpected body.");
        }
    }

    /// <summary>
    /// The short test, ProtoTest side: the same two calls and assertions through the REST client, inside a
    /// full test lifecycle.
    /// </summary>
    private static async Task ShortTestAsync(ProtoExecutionContext context)
    {
        using var created = await context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/benchmark/orders");
        created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new { product = "notebook", quantity = 2, status = "pending" });

        var id = created.ReadAsJson<JsonElement>().GetProperty("id").GetString()
            ?? throw new InvalidOperationException("The create returned no id.");

        using var read = await context.Rest().GetAsync($"/benchmark/orders/{id}");
        read
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { id, product = "notebook", quantity = 2, status = "pending" });
    }

    private static async Task PingAsync(ProtoExecutionContext context)
    {
        using var response = await context.Rest().GetAsync("/ping");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The benchmark request failed: {response.StatusCode}.");
        }
    }

    private static async Task PingRawAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/ping");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The benchmark request failed: {response.StatusCode}.");
        }
    }

    private static async Task<Overhead> MeasureProtoAsync(
        string mode,
        bool tracing,
        Func<ProtoExecutionContext, Task>? exercise)
    {
        var output = Path.Combine(Path.GetTempPath(), $"prototest-overhead-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder()
                .ConfigureTracing(options =>
                {
                    options.Enabled = tracing;
                    options.OutputPath = output;
                    options.EmbedSources = false;
                    options.EmbedArtifacts = false;
                });
            builder.AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddRest(rest => rest.AddClient("Api")));

            await using var host = builder.Build();
            await host.StartAsync();

            for (var index = 0; index < Warmup; index++)
            {
                await RunProtoAsync(host, index, exercise);
            }

            var samples = new List<double>(Iterations);
            var starts = new List<double>(Iterations);
            var calls = new List<double>(Iterations);
            var completes = new List<double>(Iterations);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

            for (var index = 0; index < Iterations; index++)
            {
                var total = Stopwatch.StartNew();
                var start = Stopwatch.StartNew();
                var context = await host.StartTestAsync(
                    $"overhead {index}",
                    (20000 + index).ToString("D5"),
                    TestMethods.Placeholder,
                    ApiAttributes);
                start.Stop();

                var call = Stopwatch.StartNew();
                if (exercise is not null)
                {
                    await exercise(context);
                }

                call.Stop();

                var complete = Stopwatch.StartNew();
                await host.CompleteTestAsync(ProtoTestResult.Passed);
                complete.Stop();
                total.Stop();

                samples.Add(total.Elapsed.TotalMilliseconds);
                starts.Add(start.Elapsed.TotalMilliseconds);
                calls.Add(call.Elapsed.TotalMilliseconds);
                completes.Add(complete.Elapsed.TotalMilliseconds);
            }

            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            await host.StopAsync();

            var overhead = Overhead.From(samples, starts, calls, completes, allocated / (double)Iterations / 1024);
            TestContext.Progress.WriteLine(Format(mode, overhead));
            return overhead;
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    private static async Task RunProtoAsync(
        ProtoHost host,
        int index,
        Func<ProtoExecutionContext, Task>? exercise)
    {
        var context = await host.StartTestAsync(
            $"warmup {index}",
            (10000 + index).ToString("D5"),
            TestMethods.Placeholder,
            ApiAttributes);
        if (exercise is not null)
        {
            await exercise(context);
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private static async Task<Overhead> MeasureRawAsync(string mode, Func<HttpClient, Task> exercise)
    {
        await using var factory = new WebApplicationFactory<SampleApi.Program>();

        for (var index = 0; index < Warmup; index++)
        {
            using var warmupClient = factory.CreateClient();
            await exercise(warmupClient);
        }

        var samples = new List<double>(Iterations);
        var starts = new List<double>(Iterations);
        var calls = new List<double>(Iterations);
        var completes = new List<double>(Iterations);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        for (var index = 0; index < Iterations; index++)
        {
            var total = Stopwatch.StartNew();
            var start = Stopwatch.StartNew();
            using var client = factory.CreateClient();
            start.Stop();

            var call = Stopwatch.StartNew();
            await exercise(client);
            call.Stop();
            completes.Add(0);
            total.Stop();

            samples.Add(total.Elapsed.TotalMilliseconds);
            starts.Add(start.Elapsed.TotalMilliseconds);
            calls.Add(call.Elapsed.TotalMilliseconds);
        }

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var overhead = Overhead.From(samples, starts, calls, completes, allocated / (double)Iterations / 1024);
        TestContext.Progress.WriteLine(Format(mode, overhead));
        return overhead;
    }

    private static async Task<double> MeasureStartupAsync(bool tracing)
    {
        var samples = new List<double>(StartupRounds);
        for (var round = 0; round < StartupRounds; round++)
        {
            var output = Path.Combine(Path.GetTempPath(), $"prototest-startup-{Guid.NewGuid():N}.prototrace");
            var builder = new ProtoHostBuilder()
                .ConfigureTracing(options =>
                {
                    options.Enabled = tracing;
                    options.OutputPath = output;
                    options.EmbedSources = false;
                    options.EmbedArtifacts = false;
                });
            builder.AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddRest(rest => rest.AddClient("Api")));

            var total = Stopwatch.StartNew();
            await using (var host = builder.Build())
            {
                // Startup runs through the first completed test so both sides pay for building the
                // in-process server, which is lazy until the first request.
                await host.StartAsync();
                var context = await host.StartTestAsync(
                    "startup",
                    "00001",
                    TestMethods.Placeholder,
                    ApiAttributes);
                await PingAsync(context);
                await host.CompleteTestAsync(ProtoTestResult.Passed);
                await host.StopAsync();
            }

            total.Stop();
            samples.Add(total.Elapsed.TotalMilliseconds);
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static async Task<double> MeasureRawStartupAsync()
    {
        var samples = new List<double>(StartupRounds);
        for (var round = 0; round < StartupRounds; round++)
        {
            var total = Stopwatch.StartNew();
            await using (var factory = new WebApplicationFactory<SampleApi.Program>())
            {
                using var client = factory.CreateClient();
                await PingRawAsync(client);
            }

            total.Stop();
            samples.Add(total.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static string Format(string mode, Overhead overhead) =>
        "[overhead] mode=" + mode +
        " median=" + F(overhead.MedianMs) + " ms" +
        " mean=" + F(overhead.MeanMs) + " ms" +
        " p95=" + F(overhead.P95Ms) + " ms" +
        " start=" + F(overhead.StartMedianMs) + " ms" +
        " call=" + F(overhead.CallMedianMs) + " ms" +
        " complete=" + F(overhead.CompleteMedianMs) + " ms" +
        " allocated=" + F(overhead.AllocatedKbPerTest, 0) + " KB";

    private static string F(double value, int digits = 3) =>
        value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private sealed record Overhead(
        double MedianMs,
        double MeanMs,
        double P95Ms,
        double StartMedianMs,
        double CallMedianMs,
        double CompleteMedianMs,
        double AllocatedKbPerTest)
    {
        public static Overhead From(
            List<double> samples,
            List<double> starts,
            List<double> calls,
            List<double> completes,
            double allocatedKbPerTest)
        {
            return new Overhead(
                Median(samples),
                samples.Average(),
                Percentile(samples, 0.95),
                Median(starts),
                Median(calls),
                Median(completes),
                allocatedKbPerTest);
        }

        private static double Median(List<double> samples)
        {
            var sorted = new List<double>(samples);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        private static double Percentile(List<double> samples, double percentile)
        {
            var sorted = new List<double>(samples);
            sorted.Sort();
            var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
        }
    }
}
