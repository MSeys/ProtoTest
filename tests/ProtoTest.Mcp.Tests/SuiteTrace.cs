namespace ProtoTest.Mcp.Tests;

using ProtoTest.Core;
using ProtoTest.TestSupport;

/// <summary>
/// Writes a run that used what a suite map lists: a capability, a REST client, a data provisioner, a
/// suite attribute and a page object, across one passed and one failed test.
/// </summary>
internal static class SuiteTrace
{
    public static async Task WriteAsync(string directory)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = Path.Combine(directory, "suite.prototrace");
            options.EmbedSources = false;
        });
        builder.AddCapability(new ProtoCapabilityDescriptor("Shop", ProtoCapabilityKinds.Server, "Shop.Tests"));

        await using var host = builder.Build();
        await host.StartAsync();

        var creating = await host.StartTestAsync("creating an order returns it", "00001", TestMethods.Placeholder);
        Record(creating, "attribute.before", ("attribute.type", "Shop.Tests.SignedInAttribute"), ("code.file.path", "tests/Shop.Tests/SignedInAttribute.cs"), ("code.function.name", "Shop.Tests.SignedInAttribute.BeforeTestAsync"));
        Record(creating, "client.initialize", ("client.name", "Shop"), ("client.protocol", "REST"), ("client.type", "ProtoRestClient"));
        Record(
            creating,
            "data.provision",
            ("data.provisioner", "Shop.Tests.CustomerProvisioner"),
            ("data.input_type", "Shop.Tests.Customer"),
            ("data.result_type", "Shop.Tests.CustomerId"),
            ("data.owned", "true"));
        Record(creating, "http.request", ("code.file.path", "tests/Shop.Tests/OrderTests.cs"), ("code.function.name", "Shop.Tests.OrderTests.PlaceholderMethod"));
        Record(creating, "web.click", ("web.component", "OrdersPage.Toolbar"), ("web.element", "NewOrder"), ("web.locator", "[data-testid=new-order]"));
        Record(creating, "assert.web", ("web.component", "OrdersPage.Order[order-41]"), ("web.element", "Status"), ("web.locator", "[data-testid=order-status]"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var failing = await host.StartTestAsync("cancelling an order refunds it", "00002", TestMethods.Placeholder);
        Record(failing, "data.provision", ("data.provisioner", "Shop.Tests.CustomerProvisioner"));
        await host.CompleteTestAsync(ProtoTestResult.Failed(new InvalidOperationException("no refund")));
        await host.StopAsync();
    }

    private static void Record(ProtoExecutionContext test, string kind, params (string Key, string Value)[] attributes)
    {
        var operation = test.Trace.Operation(kind, kind, "Shop.Tests");
        foreach (var (key, value) in attributes)
        {
            operation = operation.With(key, value);
        }

        using var _ = operation.Begin();
    }
}
