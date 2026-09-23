namespace ProtoTest.MSTest.Tests;

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Stands in for the MSTest runner: it invokes the subject like the framework would and turns the
/// outcome into a <see cref="TestResult"/>, so the adapter's mapping code runs for real.
/// </summary>
internal sealed class FakeTestMethod(MethodInfo method) : ITestMethod
{
    public string TestMethodName => method.Name;

    public string TestClassName => method.DeclaringType!.FullName!;

    public Type ReturnType => method.ReturnType;

    public object?[]? Arguments => null;

    public ParameterInfo[] ParameterTypes => method.GetParameters();

    public MethodInfo MethodInfo => method;

    public Attribute[] GetAllAttributes() => method.GetCustomAttributes().ToArray();

    public TAttributeType[] GetAttributes<TAttributeType>() where TAttributeType : Attribute
        => method.GetCustomAttributes<TAttributeType>().ToArray();

    public async Task<TestResult> InvokeAsync(object?[]? arguments)
    {
        try
        {
            var instance = Activator.CreateInstance(method.DeclaringType!);
            var returned = method.Invoke(instance, arguments);
            if (returned is Task task)
            {
                await task;
            }

            return new TestResult { Outcome = UnitTestOutcome.Passed };
        }
        catch (Exception exception)
        {
            // Reflection wraps the assertion failure, exactly like a runner that inspects the inner exception.
            var failure = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            return failure is AssertInconclusiveException
                ? new TestResult { Outcome = UnitTestOutcome.Inconclusive, TestFailureException = failure }
                : new TestResult { Outcome = UnitTestOutcome.Failed, TestFailureException = failure };
        }
    }
}
