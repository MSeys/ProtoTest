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

/// <summary>
/// Wraps the test method invocation so the ProtoTest context spans the test and sees xUnit's recorded failure.
/// </summary>
internal sealed class ProtoXunitTestRunner(
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
    : XunitTestRunner(
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
    protected override async Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
    {
        var skipReason = ProtoTestSkip.GetReason(ProtoAttributeResolver.Resolve(TestMethod), ProtoTestAssembly.Host);
        if (skipReason is not null)
        {
            // Skipping before the lifecycle starts keeps the trace honest: nothing ran, so nothing failed.
            MessageBus.QueueMessage(new TestSkipped(Test, skipReason));
            return 0m;
        }

        var host = ProtoTestAssembly.Host;
        try
        {
            await host.StartTestAsync(
                Test.DisplayName,
                TestMethod,
                ProtoAttributeResolver.Resolve(TestMethod),
                Xunit2AttachmentPublisher.Instance);
        }
        catch (Exception exception)
        {
            // A failed setup has already been rolled back and recorded by the host.
            aggregator.Add(exception);
            return 0m;
        }

        var executionTime = await base.InvokeTestMethodAsync(aggregator);

        var failure = aggregator.ToException();
        var result = CancellationTokenSource.IsCancellationRequested
            ? ProtoTestResult.Cancelled(failure)
            : failure is null ? ProtoTestResult.Passed : ProtoTestResult.Failed(failure);
        try
        {
            await host.CompleteTestAsync(result);
        }
        catch (Exception exception)
        {
            aggregator.Add(exception);
        }

        return executionTime;
    }
}
