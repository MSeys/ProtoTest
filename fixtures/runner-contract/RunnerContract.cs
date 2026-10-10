#if TUNIT
[assembly: TUnit.Core.Executors.TestExecutor<ProtoTest.TUnit.ProtoTestExecutor>]
#elif MSTEST
[assembly: Microsoft.VisualStudio.TestTools.UnitTesting.DoNotParallelize]
#elif XUNIT3
[assembly: Xunit.AssemblyFixture(typeof(RunnerContract.Setup))]
#endif

namespace RunnerContract;

using ProtoTest.Core;
#if TUNIT
using TUnit.Core;
using Case = TUnit.Core.TestAttribute;
using Row = TUnit.Core.ArgumentsAttribute;
using Rows = TUnit.Core.TestAttribute;
#elif NUNIT
using Case = ProtoTest.NUnit.ProtoTestAttribute;
using Row = NUnit.Framework.TestCaseAttribute;
using Rows = ProtoTest.NUnit.ProtoTestAttribute;
#elif MSTEST
using Case = ProtoTest.MSTest.ProtoTestAttribute;
using Row = Microsoft.VisualStudio.TestTools.UnitTesting.DataRowAttribute;
using Rows = ProtoTest.MSTest.ProtoTestAttribute;
#elif XUNIT
using Case = ProtoTest.Xunit.ProtoTestFactAttribute;
using Row = Xunit.InlineDataAttribute;
using Rows = ProtoTest.Xunit.ProtoTestTheoryAttribute;
#else
using Case = ProtoTest.Xunit3.ProtoTestFactAttribute;
using Row = Xunit.InlineDataAttribute;
using Rows = ProtoTest.Xunit3.ProtoTestTheoryAttribute;
#endif

#if TUNIT
public sealed class Setup : ProtoTest.TUnit.ProtoTestAssembly
{
    [Before(HookType.Assembly)]
    public static Task Start(AssemblyHookContext context) => InitializeAsync(Scenario.Configure);

    [After(HookType.Assembly)]
    public static Task Stop(AssemblyHookContext context) => CleanupAsync();
}
#elif MSTEST
[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
public sealed class Setup : ProtoTest.MSTest.ProtoTestAssembly
{
    [Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitialize]
    public static Task Start(Microsoft.VisualStudio.TestTools.UnitTesting.TestContext context) => InitializeAsync(Scenario.Configure);

    [Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanup]
    public static Task Stop() => CleanupAsync();
}
#else
#if NUNIT
[NUnit.Framework.SetUpFixture]
public sealed class Setup : ProtoTest.NUnit.ProtoTestAssembly
#elif XUNIT
public sealed class Setup : ProtoTest.Xunit.ProtoTestAssembly
#else
public sealed class Setup : ProtoTest.Xunit3.ProtoTestAssembly
#endif
{
    protected override void Configure(IProtoHostBuilder builder) => Scenario.Configure(builder);
}
#endif

#if XUNIT
[Xunit.CollectionDefinition(Name)]
public sealed class ContractCollection : Xunit.ICollectionFixture<Setup>
{
    public const string Name = "runner-contract";
}
#endif

/// <summary>
/// The scenario a run plays, read from the environment the driver sets: what the body does, whether
/// setup and cleanup fail, the cleanup policy, and the folder the trace and the markers go to.
/// </summary>
public static class Scenario
{
    public const string SetupMessage = "CONTRACT-SETUP";
    public const string BodyMessage = "CONTRACT-BODY";
    public const string CleanupMessage = "CONTRACT-CLEANUP";
    public const string SkipMessage = "CONTRACT-SKIP";

    public static string Body => Environment.GetEnvironmentVariable("RUNNER_CONTRACT_BODY") ?? "pass";

    public static bool CleanupFails => Environment.GetEnvironmentVariable("RUNNER_CONTRACT_CLEANUP") == "fail";

    public static bool SetupFails => Environment.GetEnvironmentVariable("RUNNER_CONTRACT_SETUP") == "fail";

    public static string Output => Environment.GetEnvironmentVariable("RUNNER_CONTRACT_OUTPUT")
        ?? throw new InvalidOperationException("RUNNER_CONTRACT_OUTPUT is not set; the runner contract tests set it.");

    private static readonly ProtoLock MarkGate = new();

    /// <summary>Appends a marker; rows that run in parallel share the file.</summary>
    public static void Mark(string marker)
    {
        lock (MarkGate)
        {
            File.AppendAllText(Path.Combine(Output, "markers.txt"), marker + Environment.NewLine);
        }
    }

    public static void Configure(IProtoHostBuilder builder)
    {
        builder.ConfigureTracing(options => options.OutputPath = Path.Combine(Output, "contract.prototrace"));
        builder.ConfigureCleanup(options => options.CleanupFailures =
            Environment.GetEnvironmentVariable("RUNNER_CONTRACT_MODE") == "Report"
                ? ProtoCleanupFailureMode.Report
                : ProtoCleanupFailureMode.Fail);
        builder.AddTestHook<SetupHook>();
    }

    /// <summary>Runs the body the scenario names, with two resources whose release the driver checks.</summary>
    public static void Run()
    {
        Proto.Context.RegisterResource("good", "contract", "cleanup must continue", _ =>
        {
            Mark("good-release");
            return ValueTask.CompletedTask;
        });
        Proto.Context.RegisterResource("bad", "contract", "the injected cleanup failure", _ =>
        {
            Mark("bad-release");
            return CleanupFails ? throw new InvalidOperationException(CleanupMessage) : ValueTask.CompletedTask;
        });
        Mark("body");
        switch (Body)
        {
            case "pass":
                return;
            case "fail":
                throw new InvalidOperationException(BodyMessage);
            case "cancel":
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }

                return;
            case "skip":
#if TUNIT
                Skip.Test(SkipMessage);
#elif NUNIT
                NUnit.Framework.Assert.Ignore(SkipMessage);
#elif MSTEST
                Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Inconclusive(SkipMessage);
#elif XUNIT3
                Xunit.Assert.Skip(SkipMessage);
#else
                throw new InvalidOperationException("xUnit v2 has no runtime skip; the matrix leaves this case out.");
#endif
#if !XUNIT
                return;
#endif
            default:
                throw new InvalidOperationException($"Unknown body '{Body}'.");
        }
    }

    /// <summary>Fails setup before the body when the scenario asks for it.</summary>
    public sealed class SetupHook : IProtoTestHook
    {
        public Task BeforeTestAsync(ProtoExecutionContext context)
            => SetupFails ? Task.FromException(new InvalidOperationException(SetupMessage)) : Task.CompletedTask;

        public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
    }
}

#if XUNIT
[Xunit.Collection(ContractCollection.Name)]
#elif MSTEST
[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
#endif
public sealed class CleanupContract
{
    [Case]
    public void Body() => Scenario.Run();
}

#if XUNIT
[Xunit.Collection(ContractCollection.Name)]
#elif MSTEST
[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
#endif
public sealed class RowsContract
{
    [Rows]
    [Row(1)]
    [Row(2)]
    public void Each(int row)
    {
        Scenario.Mark($"row-{row}");
        _ = Proto.Context.TestName;
    }
}

#if XUNIT
/// <summary>ProtoTest tests outside the collection that starts the host: each must fail, never pass.</summary>
public sealed class MissingFixtureContract
{
    [Case]
    public void First()
    {
    }

    [Case]
    public void Second()
    {
    }
}
#endif
