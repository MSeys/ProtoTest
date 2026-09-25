namespace ProtoTest.AspNetCore.Tests;

using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Rest;

[TestFixture]
public sealed class ClockTests
{
    [Test]
    public async Task Application_ShouldSeeTheTestsClock()
    {
        var seed = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var builder = new ProtoHostBuilder();
        builder.ConfigureClock(new ProtoClock(seed));
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<SampleApi.Program>()
            .AddRest(rest => rest.AddClient("Api")));
        await using var host = builder.Build();
        await host.StartAsync();

        var context = await host.StartTestAsync(
            "clock",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api", "Rest:Api")]);
        context.Clock.Advance(TimeSpan.FromHours(3));

        using var response = await context.Rest().GetAsync("/time");
        var utcNow = response.ReadAsJson<JsonElement>().GetProperty("utcNow").GetDateTimeOffset();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(
            utcNow,
            Is.EqualTo(seed.AddHours(3)),
            "the in-process application resolves the active test's clock");
    }

    [Test]
    public async Task Application_WhenTwoHostsShareATestId_ShouldSeeEachHostsClock()
    {
        var firstSeed = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var secondSeed = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        const long RunPrefix = 424242;

        await using var first = ClockApplication(firstSeed, RunPrefix).Build();
        await using var second = ClockApplication(secondSeed, RunPrefix).Build();
        await first.StartAsync();
        await second.StartAsync();

        var firstStarted = new TaskCompletionSource<ProtoExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<ProtoExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequestedAt = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequestedAt = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstRun = Task.Run(async () =>
        {
            var context = await first.StartTestAsync(
                "first", TestMethods.Placeholder, [new ApplicationAttribute("Api", "Rest:Api")]);
            firstStarted.SetResult(context);
            await release.Task.ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromHours(3));
            using var response = await context.Rest().GetAsync("/time");
            firstRequestedAt.SetResult(response.ReadAsJson<JsonElement>().GetProperty("utcNow").GetDateTimeOffset());
            await first.CompleteTestAsync(ProtoTestResult.Passed);
        });
        var secondRun = Task.Run(async () =>
        {
            var context = await second.StartTestAsync(
                "second", TestMethods.Placeholder, [new ApplicationAttribute("Api", "Rest:Api")]);
            secondStarted.SetResult(context);
            await release.Task.ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromHours(5));
            using var response = await context.Rest().GetAsync("/time");
            secondRequestedAt.SetResult(response.ReadAsJson<JsonElement>().GetProperty("utcNow").GetDateTimeOffset());
            await second.CompleteTestAsync(ProtoTestResult.Passed);
        });

        var firstContext = await firstStarted.Task;
        var secondContext = await secondStarted.Task;
        Assert.That(
            firstContext.TestId,
            Is.EqualTo(secondContext.TestId),
            "the hosts share the configured run prefix, so both requests carry the same test id");

        release.SetResult();
        await Task.WhenAll(firstRun, secondRun);
        var firstUtcNow = await firstRequestedAt.Task;
        var secondUtcNow = await secondRequestedAt.Task;

        Assert.Multiple(() =>
        {
            Assert.That(
                firstUtcNow,
                Is.EqualTo(firstSeed.AddHours(3)),
                "the first host's request resolves the first host's clock");
            Assert.That(
                secondUtcNow,
                Is.EqualTo(secondSeed.AddHours(5)),
                "the second host's request resolves the second host's clock");
        });

        await first.StopAsync();
        await second.StopAsync();
    }

    private static ProtoHostBuilder ClockApplication(DateTimeOffset seed, long runPrefix)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureClock(new ProtoClock(seed));
        builder.ConfigureTestIds(options => options.RunPrefix = runPrefix);
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<SampleApi.Program>()
            .AddRest(rest => rest.AddClient("Api")));
        return builder;
    }
}
