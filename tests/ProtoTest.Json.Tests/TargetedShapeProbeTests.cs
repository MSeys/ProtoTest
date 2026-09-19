namespace ProtoTest.Json.Tests;

using System.Reflection;
using ProtoTest.Core;
using ProtoTest.Json;

[TestFixture]
public sealed class TargetedShapeProbeTests
{
    [Test]
    public async Task CyclicShapeWithSeveralSelfReferences_ShouldDescribeWithoutBlowingUp()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "wide cyclic shape", "01", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var node = new WideNode { Name = "root" };
        node.First = node;
        node.Second = node;
        node.Third = node;
        node.Fourth = node;

        var work = Task.Run(() => Assert.Throws<JsonShapeMismatchException>(() => ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert wide cyclic shape"),
            """{"name":"root"}""",
            node)));

        var completed = work.Wait(TimeSpan.FromSeconds(10));
        Assert.That(completed, Is.True, "a cyclic shape must be described within a bounded budget");

        await host.CompleteTestAsync();
        await host.StopAsync();
    }

    private sealed class WideNode
    {
        public string Name { get; set; } = string.Empty;
        public WideNode? First { get; set; }
        public WideNode? Second { get; set; }
        public WideNode? Third { get; set; }
        public WideNode? Fourth { get; set; }
    }
}
