namespace ProtoTest.Xunit.Sdk;

using System.ComponentModel;
using System.Reflection;
using global::Xunit.Abstractions;
using global::Xunit.Sdk;
using ProtoTest.Core;

// xUnit discovers, serializes and recreates these types by name, so they must stay public.

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtoTestFactDiscoverer(IMessageSink diagnosticMessageSink) : FactDiscoverer(diagnosticMessageSink)
{
    protected override IXunitTestCase CreateTestCase(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo factAttribute)
        => new ProtoXunitTestCase(
            DiagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod);
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtoTestTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo theoryAttribute,
        object[] dataRow)
        =>
        [
            new ProtoXunitTestCase(
                DiagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                dataRow)
        ];

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo theoryAttribute)
        =>
        [
            new ProtoXunitTheoryTestCase(
                DiagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod)
        ];
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtoXunitTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ProtoXunitTestCase()
    {
    }

    public ProtoXunitTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    public override Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => new ProtoXunitTestCaseRunner(
            this,
            DisplayName,
            SkipReason,
            constructorArguments,
            TestMethodArguments,
            messageBus,
            aggregator,
            cancellationTokenSource).RunAsync();
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtoXunitTheoryTestCase : XunitTheoryTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ProtoXunitTheoryTestCase()
    {
    }

    public ProtoXunitTheoryTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
    }

    public override Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => new ProtoXunitTheoryTestCaseRunner(
            this,
            DisplayName,
            SkipReason,
            constructorArguments,
            diagnosticMessageSink,
            messageBus,
            aggregator,
            cancellationTokenSource).RunAsync();
}

internal static class ProtoXunitRunnerFactory
{
    /// <summary>The one place the runner's argument plumbing exists; both case runners return it.</summary>
    internal static XunitTestRunner Create(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => new ProtoXunitTestRunner(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource);
}

internal sealed class ProtoXunitTestCaseRunner(
    IXunitTestCase testCase,
    string displayName,
    string skipReason,
    object[] constructorArguments,
    object[] testMethodArguments,
    IMessageBus messageBus,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource)
    : XunitTestCaseRunner(
        testCase,
        displayName,
        skipReason,
        constructorArguments,
        testMethodArguments,
        messageBus,
        aggregator,
        cancellationTokenSource)
{
    protected override XunitTestRunner CreateTestRunner(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => ProtoXunitRunnerFactory.Create(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource);
}

internal sealed class ProtoXunitTheoryTestCaseRunner(
    IXunitTestCase testCase,
    string displayName,
    string skipReason,
    object[] constructorArguments,
    IMessageSink diagnosticMessageSink,
    IMessageBus messageBus,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource)
    : XunitTheoryTestCaseRunner(
        testCase,
        displayName,
        skipReason,
        constructorArguments,
        diagnosticMessageSink,
        messageBus,
        aggregator,
        cancellationTokenSource)
{
    protected override XunitTestRunner CreateTestRunner(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => ProtoXunitRunnerFactory.Create(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource);
}

/// <summary>
/// Wraps the test method invocation so the ProtoTest context spans the test and sees xUnit's recorded failure.
/// </summary>
internal sealed class ProtoXunitTestRunner : XunitTestRunner
{
    private readonly ProtoTestPreparation _preparation;

    public ProtoXunitTestRunner(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource)
    {
        // Preparing in the constructor resolves the attributes once and decides the skip before
        // invocation; the runner for a row starts from the very same resolution.
        _preparation = ProtoTestAdapter.Prepare(testMethod, ProtoTestAssembly.Host, Test.DisplayName);

        // xUnit v2 has no dynamic skip API and its non-virtual RunAsync decides pass/fail from the
        // invoker's aggregator, so a TestSkipped queued from InvokeTestMethodAsync would still be
        // counted and announced as TestPassed. RunAsync checks SkipReason before invoking anything,
        // so the dynamic skip belongs there: skipping before the lifecycle starts keeps the trace
        // honest - nothing ran, so nothing failed.
        SkipReason ??= _preparation.SkipReason;
    }

    protected override async Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
    {
        var host = ProtoTestAssembly.Host;
        ProtoTestScope scope;
        try
        {
            scope = await ProtoTestScope.StartAsync(
                _preparation, host, Xunit2AttachmentPublisher.Instance, CancellationTokenSource.Token);
        }
        catch (Exception exception)
        {
            // A failed setup has already been rolled back and recorded by the host.
            aggregator.Add(exception);
            return 0m;
        }

        return await InvokeAndCompleteAsync(
            scope,
            aggregator,
            CancellationTokenSource,
            () => base.InvokeTestMethodAsync(aggregator));
    }

    /// <summary>
    /// Invokes the inner runner and completes the scope in a <c>finally</c>: however the invocation
    /// and the mapping end, the started test completes once. The invocation is a delegate so that
    /// path is provable - xUnit aggregates every throw a body or lifecycle attribute produces, so a
    /// test body cannot leave this method without a result, but the runner itself throwing is
    /// exactly what the <c>finally</c> protects against (ScopeCompletionTests drives it).
    /// </summary>
    internal static async Task<decimal> InvokeAndCompleteAsync(
        ProtoTestScope scope,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        Func<Task<decimal>> invocation)
    {
        try
        {
            var executionTime = await invocation();

            var failure = aggregator.ToException();
            // The runner's own cancellation source keeps xUnit's CTS semantics; a body that threw an
            // OperationCanceledException is classified by the shared rule.
            scope.Result = cancellationTokenSource.IsCancellationRequested
                ? ProtoTestResult.Cancelled(failure)
                : failure is null ? ProtoTestResult.Passed : ProtoTestResult.FromException(failure);

            return executionTime;
        }
        finally
        {
            await scope.DisposeAsync();
        }
    }
}

