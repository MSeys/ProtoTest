namespace ProtoTest.Mcp.Tests;

using System.Text.Json;

[TestFixture]
public sealed class SuiteMapToolTests
{
    [Test]
    public async Task GetSuiteMap_ShouldListWhatTheRunComposedAndUsed()
    {
        using var folder = new TestProjectFolder();
        await SuiteTrace.WriteAsync(folder.Path);
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var map = await session.CallJsonAsync("get_suite_map");

        var provisioner = Entries(map, "provisioners").Single();
        var attribute = Entries(map, "attributes").Single();
        var page = Entries(map, "pages").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.GetProperty("tests").GetInt32(), Is.EqualTo(2));
            Assert.That(Entries(map, "capabilities").Select(entry => entry.GetProperty("name").GetString()), Does.Contain("Shop"));
            Assert.That(Entries(map, "clients").Single().GetProperty("protocol").GetString(), Is.EqualTo("REST"));
            Assert.That(provisioner.GetProperty("provisioner").GetString(), Is.EqualTo("Shop.Tests.CustomerProvisioner"));
            Assert.That(provisioner.GetProperty("input").GetString(), Is.EqualTo("Shop.Tests.Customer"));
            Assert.That(provisioner.GetProperty("owned").GetBoolean(), Is.True);
            Assert.That(provisioner.GetProperty("usedByTests").GetInt32(), Is.EqualTo(2));
            Assert.That(attribute.GetProperty("suite").GetBoolean(), Is.True, "a suite's own attribute is marked for reuse");
            Assert.That(page.GetProperty("page").GetString(), Is.EqualTo("OrdersPage"));
            Assert.That(
                page.GetProperty("elements").EnumerateArray().Select(element => element.GetProperty("path").GetString()),
                Is.EqualTo(new[] { "OrdersPage.Toolbar.NewOrder", "OrdersPage.Order[key].Status" }),
                "a keyed element lists its shape, not the key one run found it by");
        }
    }

    [Test]
    public async Task GetSuiteMap_ShouldTakeExamplesOnlyFromPassedTests()
    {
        using var folder = new TestProjectFolder();
        await SuiteTrace.WriteAsync(folder.Path);
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var examples = Entries(await session.CallJsonAsync("get_suite_map"), "examples")
            .ToDictionary(entry => entry.GetProperty("work").GetString()!, entry => entry.GetProperty("test").GetString());

        Assert.That(examples, Is.EquivalentTo(new Dictionary<string, string?>
        {
            ["REST call"] = "creating an order returns it",
            ["browser journey"] = "creating an order returns it",
            ["test data"] = "creating an order returns it"
        }));
    }

    [Test]
    public async Task GetSuiteMap_ShouldPointAnExampleAtTheTestMethodsFile()
    {
        using var folder = new TestProjectFolder();
        await SuiteTrace.WriteAsync(folder.Path);
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var examples = Entries(await session.CallJsonAsync("get_suite_map"), "examples").ToArray();

        Assert.That(
            examples.Select(entry => entry.GetProperty("sourceFile").GetString()),
            Is.All.EqualTo("tests/Shop.Tests/OrderTests.cs"),
            "the attribute's file is not the test's");
    }

    [Test]
    public async Task GetSuiteMap_ShouldListTheReportsUncoveredUnits()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var gaps = (await session.CallJsonAsync("get_suite_map")).GetProperty("coverageGaps");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gaps.GetProperty("total").GetInt32(), Is.EqualTo(2));
            Assert.That(
                gaps.GetProperty("units").EnumerateArray().Select(unit => unit.GetProperty("identifier").GetString()),
                Is.EquivalentTo(new[] { "GET /api/v1/orders › $.total", "POST /api/v1/orders" }));
        }
    }

    [Test]
    public async Task GetSuiteMap_WithoutAReport_ShouldSaySoInsteadOfListingNoGaps()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-passed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var map = await session.CallJsonAsync("get_suite_map");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.GetProperty("coverageGaps").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(map.GetProperty("coverageNote").GetString(), Does.Contain("No JSON report artifact"));
        }
    }

    private static IEnumerable<JsonElement> Entries(JsonElement map, string list)
        => map.GetProperty(list).GetProperty("entries").EnumerateArray();
}
