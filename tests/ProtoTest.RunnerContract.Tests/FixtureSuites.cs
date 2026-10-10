namespace ProtoTest.RunnerContract.Tests;

using System.Diagnostics;
using System.Text;
using ProtoTest.Traces;

/// <summary>The runners the fixture suites cover, one project each under <c>fixtures/runner-contract</c>.</summary>
public enum Runner
{
    NUnit,
    MSTest,
    Xunit,
    Xunit3,
    TUnit
}

/// <summary>
/// The fixture suites under <c>fixtures/runner-contract</c>: one intentionally failing test application per
/// runner, outside the solution so no ordinary run discovers them. Built once per test run, in the
/// configuration this project was built in; each scenario then runs in a fresh process of its own.
/// </summary>
internal static class FixtureSuites
{
    private static readonly Lazy<Task> Built = new(BuildAsync);

    /// <summary>The repository root, found from the test's output folder.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The folder holding the fixture suites and their solution.</summary>
    public static string Root { get; } = Path.Combine(RepositoryRoot, "fixtures", "runner-contract");

    /// <summary>The configuration this project was built in, which the fixture suites are built in too.</summary>
    public static string Configuration { get; } =
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    /// <summary>
    /// Runs one test class of a runner's suite in a fresh process, with the scenario in its environment,
    /// and reads back what the runner reported and the trace archive ProtoTest wrote.
    /// </summary>
    public static async Task<ScenarioRun> RunAsync(Runner runner, string testClass, Scenario scenario)
    {
        await Built.Value;
        var output = Directory.CreateTempSubdirectory("prototest-runner-contract-").FullName;
        try
        {
            var assembly = Path.Combine(Root, runner.ToString(), "bin", Configuration, "net8.0", $"RunnerContract.{runner}.dll");
            var resultPath = Path.Combine(output, runner == Runner.Xunit3 ? "result.xml" : "result.trx");
            var fullName = $"RunnerContract.{testClass}";
            string[] arguments = runner switch
            {
                Runner.Xunit3 => [assembly, "-class", fullName, "-result-xml", resultPath, "-noColor"],
                Runner.TUnit =>
                [
                    assembly, "--treenode-filter", $"/*/*/{testClass}/*", "--report-trx", "--report-trx-filename",
                    "result.trx", "--results-directory", output, "--progress", "off"
                ],
                _ =>
                [
                    "test", assembly, "--filter", $"FullyQualifiedName~{fullName}.", "--logger",
                    "trx;LogFileName=result.trx", "--results-directory", output
                ]
            };

            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RUNNER_CONTRACT_OUTPUT"] = output,
                ["RUNNER_CONTRACT_BODY"] = scenario.Body,
                ["RUNNER_CONTRACT_CLEANUP"] = scenario.CleanupFails ? "fail" : "ok",
                ["RUNNER_CONTRACT_MODE"] = scenario.Mode,
                ["RUNNER_CONTRACT_SETUP"] = scenario.SetupFails ? "fail" : "ok"
            };
            var (exitCode, log) = await RunDotnetAsync(output, environment, arguments);
            var results = File.Exists(resultPath) ? RunnerResults.Read(resultPath) : [];
            var tracePath = Path.Combine(output, "contract.prototrace");
            var trace = File.Exists(tracePath) ? ProtoTraceArchive.Open(tracePath) : null;
            var markersPath = Path.Combine(output, "markers.txt");
            var markers = File.Exists(markersPath) ? await File.ReadAllLinesAsync(markersPath) : [];
            return new ScenarioRun(exitCode, log, results, trace, markers);
        }
        finally
        {
            Delete(output);
        }
    }

    private static async Task BuildAsync()
    {
        var solution = Path.Combine(Root, "RunnerContract.slnx");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["MSBUILDDISABLENODEREUSE"] = "1" };
        var (exitCode, output) = await RunDotnetAsync(
            Root,
            environment,
            "build",
            solution,
            "--configuration",
            Configuration,
            "--nologo",
            "--verbosity",
            "quiet");
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"The runner contract fixtures did not build:{Environment.NewLine}{output}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        params string[] arguments)
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
        start.Environment["TESTINGPLATFORM_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        var reading = Task.WhenAll(CopyAsync(process.StandardOutput, output), CopyAsync(process.StandardError, output));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} did not finish in five minutes:{Environment.NewLine}{output}");
        }

        // A build can leave a process behind that holds the output open; what the command wrote is complete once it exited.
        await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(2)));
        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    private static async Task CopyAsync(StreamReader reader, StringBuilder into)
    {
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                lock (into)
                {
                    into.Append(buffer, 0, read);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The process was let go while a holder of its output lived on.
        }
    }

    private static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A runner file can stay locked for a moment after the process exits; the folder is under temp.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProtoTest.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}

/// <summary>What a scenario asks the fixture's test to do.</summary>
/// <param name="Body">The body: <c>pass</c>, <c>fail</c>, <c>skip</c> (a runtime skip after the context started) or <c>cancel</c>.</param>
/// <param name="CleanupFails">Whether one of the two resources the body registers fails to release.</param>
/// <param name="Mode">The cleanup failure policy: <c>Fail</c> or <c>Report</c>.</param>
/// <param name="SetupFails">Whether a test hook fails before the body runs.</param>
public sealed record Scenario(string Body = "pass", bool CleanupFails = false, string Mode = "Fail", bool SetupFails = false)
{
    /// <inheritdoc />
    public override string ToString()
        => SetupFails ? "setup fails" : $"body {Body}, cleanup {(CleanupFails ? "fails" : "ok")}, {Mode}";
}

/// <summary>One finished scenario: the runner's exit code and output, its reported tests, the trace and the markers.</summary>
internal sealed record ScenarioRun(
    int ExitCode,
    string Output,
    IReadOnlyList<RunnerResult> Results,
    ProtoTraceArchive? Trace,
    IReadOnlyList<string> Markers);
