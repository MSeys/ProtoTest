namespace ProtoTest.Mcp.Tests;

/// <summary>The stdio host's configuration surface: the discovery precedence and its named errors.</summary>
[TestFixture]
public sealed class McpConfigurationTests
{
    private static readonly string CurrentDirectory = Path.Combine(Path.GetTempPath(), "prototest-mcp-config");

    [Test]
    public void Resolve_ShouldPreferATraceFileOverEverythingElse()
    {
        var options = ProtoTestMcpOptions.Resolve(
            ["--trace", "one.prototrace", "--project", "project-dir"],
            environmentProjectDirectory: "env-dir",
            currentDirectory: CurrentDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.TracePath, Is.EqualTo(Path.GetFullPath("one.prototrace", CurrentDirectory)));
            Assert.That(options.ProjectDirectory, Is.Null);
        }
    }

    [Test]
    public void Resolve_ShouldPreferTheProjectArgumentOverTheEnvironment()
    {
        var options = ProtoTestMcpOptions.Resolve(
            ["--project", "project-dir"],
            environmentProjectDirectory: "env-dir",
            currentDirectory: CurrentDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.TracePath, Is.Null);
            Assert.That(options.ProjectDirectory, Is.EqualTo(Path.GetFullPath("project-dir", CurrentDirectory)));
        }
    }

    [Test]
    public void Resolve_ShouldUseTheEnvironmentThenTheCurrentDirectory()
    {
        var fromEnvironment = ProtoTestMcpOptions.Resolve(
            [],
            environmentProjectDirectory: "env-dir",
            currentDirectory: CurrentDirectory);
        var fromCurrent = ProtoTestMcpOptions.Resolve(
            [],
            environmentProjectDirectory: "  ",
            currentDirectory: CurrentDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fromEnvironment.ProjectDirectory, Is.EqualTo(Path.GetFullPath("env-dir", CurrentDirectory)));
            Assert.That(fromCurrent.ProjectDirectory, Is.EqualTo(Path.GetFullPath(CurrentDirectory)));
        }
    }

    [Test]
    public void Resolve_ShouldAcceptTheEqualsForm()
    {
        var options = ProtoTestMcpOptions.Resolve(
            ["--trace=one.prototrace"],
            environmentProjectDirectory: null,
            currentDirectory: CurrentDirectory);

        Assert.That(options.TracePath, Is.EqualTo(Path.GetFullPath("one.prototrace", CurrentDirectory)));
    }

    [Test]
    public void Resolve_ShouldNameAnUnknownArgumentOrAMissingValue()
    {
        var unknown = Assert.Throws<ProtoTestMcpConfigurationException>(() => ProtoTestMcpOptions.Resolve(
            ["--verbose"],
            environmentProjectDirectory: null,
            currentDirectory: CurrentDirectory));
        var missing = Assert.Throws<ProtoTestMcpConfigurationException>(() => ProtoTestMcpOptions.Resolve(
            ["--trace"],
            environmentProjectDirectory: null,
            currentDirectory: CurrentDirectory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknown!.Message, Does.Contain("--verbose"));
            Assert.That(missing!.Message, Does.Contain("needs a value"));
        }
    }

    [Test]
    public async Task StdioHost_ShouldReportAConfigurationErrorOnStderrAndExitTwo()
    {
        using var error = new StringWriter();

        var exit = await McpHost.RunAsync(["--nope"], error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.Contain("--nope"));
            Assert.That(error.ToString(), Does.Contain("usage: prototest-mcp"));
        }
    }

    [Test]
    public async Task StdioHost_ShouldPrintUsageForHelpAndExitTwo()
    {
        using var error = new StringWriter();

        var exit = await McpHost.RunAsync(["--help"], error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.Contain("--help"));
            Assert.That(error.ToString(), Does.Contain(ProtoTestMcpOptions.Usage));
        }
    }
}
