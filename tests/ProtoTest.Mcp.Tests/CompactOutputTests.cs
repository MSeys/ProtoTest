namespace ProtoTest.Mcp.Tests;

/// <summary>
/// What every tool's JSON leaves out so an agent spends its context on the evidence: paths are relative to
/// the folder the server reads, readable characters are not escaped, and nothing is said twice.
/// </summary>
[TestFixture]
public sealed class CompactOutputTests
{
    [Test]
    public async Task Paths_ShouldBeRelativeToTheProjectFolder()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var listing = await session.CallJsonAsync("list_runs");
        var failure = await session.CallJsonAsync("get_failure");

        var root = listing.GetProperty("root").GetString()!;
        var traceFile = listing.GetProperty("runs")[0].GetProperty("traceFile").GetString()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Path.IsPathRooted(root), Is.True, "the root stays absolute, so every relative path resolves against it");
            Assert.That(traceFile, Is.EqualTo("TestResults/run-failed.prototrace"));
            Assert.That(File.Exists(Path.Combine(root, traceFile)), Is.True);
            Assert.That(failure.GetProperty("traceFile").GetString(), Is.EqualTo("TestResults/run-failed.prototrace"));
        }
    }

    [Test]
    public async Task Text_ShouldNotEscapeQuotesPlusSignsOrSymbols()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var text = McpSession.Text(await session.CallToolAsync("get_failure"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain(Escaped("0022")));
            Assert.That(text, Does.Not.Contain(Escaped("002B")));
            Assert.That(text, Does.Not.Contain(Escaped("2022")));
            Assert.That(text, Does.Contain(char.ConvertFromUtf32(0x2022)), "the shape error's bullet reads as itself");
        }
    }

    [Test]
    public async Task GetFailure_ShouldNotRepeatTheSelectedFailureInFailedOperations()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var failure = await session.CallJsonAsync("get_failure");

        var selected = failure.GetProperty("failure").GetProperty("spanId").GetString();
        var others = failure.GetProperty("failedOperations").EnumerateArray()
            .Select(operation => operation.GetProperty("spanId").GetString());
        Assert.That(others, Does.Not.Contain(selected));
    }

    [Test]
    public async Task GetDiagnosis_ShouldDropAttributesThatRepeatTheSourceLocation()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var diagnosis = await session.CallJsonAsync("get_diagnosis", new() { ["detail"] = "context" });

        var failure = diagnosis.GetProperty("failure");
        var attributes = failure.GetProperty("attributes").EnumerateObject().Select(attribute => attribute.Name).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.GetProperty("sourceFile").GetString(), Is.Not.Empty, "the location is still there, once");
            Assert.That(attributes, Has.None.StartsWith("code."));
            Assert.That(attributes, Does.Not.Contain("expected.type"));
            Assert.That(attributes, Does.Contain("shape.expected"), "attributes that carry evidence stay");
        }
    }

    // Built from parts, so this file holds no escape sequence of its own.
    private static string Escaped(string code) => new string((char)92, 1) + "u" + code;
}
