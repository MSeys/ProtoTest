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

}
