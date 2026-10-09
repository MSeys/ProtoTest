namespace ProtoTest.Xunit.Tests;

using System.Diagnostics;
using System.Xml.Linq;

/// <summary>
/// A missing collection fixture used to escape the runner and leave <c>dotnet test</c> green.
/// The probe is a separate process because that exit code is what the runner reports, not what an
/// in-process <c>RunAsync</c> throws back to its caller.
/// </summary>
public sealed class MissingCollectionFixtureTests
{
    [Fact(Timeout = 180_000)]
    public async Task MissingCollectionFixture_ShouldFailTheProtoTestAndKeepThePlainTest()
    {
        var root = RepositoryRoot();
        var xunitProject = Path.Combine(root, "src", "ProtoTest.Xunit", "ProtoTest.Xunit.csproj");
        var directory = Path.Combine(Path.GetTempPath(), "prototest-missing-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "MissingCollectionFixture.csproj");
            var results = Path.Combine(directory, "results");
            await File.WriteAllTextAsync(project, Project(xunitProject));
            await File.WriteAllTextAsync(Path.Combine(directory, "Probe.cs"), Probe);

            var configuration = typeof(ProtoTestAssembly).Assembly.Location.Contains(
                $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
                ? "Release"
                : "Debug";
            var (exitCode, output) = await RunDotnetAsync(
                directory,
                "test",
                project,
                "--configuration",
                configuration,
                "--framework",
                "net8.0",
                "--nologo",
                "--verbosity",
                "minimal",
                "--results-directory",
                results,
                "--logger",
                "trx;LogFileName=results.trx");

            var trxPath = Directory.GetFiles(results, "*.trx", SearchOption.AllDirectories).Single();
            var document = XDocument.Load(trxPath);
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            var outcomes = document.Descendants(ns + "UnitTestResult").ToList();
            var failed = outcomes.Single(result => Attribute(result, "outcome") == "Failed");
            var passed = outcomes.Single(result => Attribute(result, "outcome") == "Passed");
            var message = failed.Descendants(ns + "Message").Single().Value;
            var total = (int)document.Descendants(ns + "Counters").Single().Attribute("total")!;

            Assert.True(exitCode != 0, output);
            Assert.Equal(2, outcomes.Count);
            Assert.Equal(2, total);
            Assert.Contains("Control", Attribute(passed, "testName"), StringComparison.Ordinal);
            Assert.Contains("MissingFixtureShouldBeOneFailedTest", Attribute(failed, "testName"), StringComparison.Ordinal);
            Assert.Contains("ProtoHost is not initialized", message, StringComparison.Ordinal);
            Assert.Contains("collection fixture", message, StringComparison.Ordinal);
            Assert.Contains("ProtoTestAssembly", message, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A runner file can stay locked for a moment after the process exits; the folder is under temp.
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProtoTest.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the ProtoTest repository from the test output.");
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await stdout) + (await stderr));
    }

    private static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? string.Empty;

    private static string Project(string xunitProject) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>disable</Nullable>
            <IsPackable>false</IsPackable>
            <IsTestProject>true</IsTestProject>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="{xunitProject}" />
          </ItemGroup>
        </Project>
        """;

    private const string Probe =
        """
        using ProtoTest.Xunit;
        public class MissingCollection
        {
            [Xunit.Fact]
            public void Control() { Xunit.Assert.True(true); }

            [ProtoTestFact]
            public void MissingFixtureShouldBeOneFailedTest() { }
        }
        """;
}
