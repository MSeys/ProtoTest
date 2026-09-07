namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using global::NUnit.Framework.Interfaces;
using ProtoTest.Core;

/// <summary>
/// NUnit test attribute that manages the <see cref="ProtoExecutionContext"/> lifecycle for each test method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ProtoTestAttribute : TestAttribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        var attributes = GetProtoAttributes(test);
        var testId = ProtoTestIdGenerator.Generate(test.Method!.MethodInfo);

        ProtoTestAssembly.Host
            .StartTestAsync(test.FullName, testId, test.Method!.MethodInfo, attributes)
            .GetAwaiter()
            .GetResult();
    }

    public void AfterTest(ITest test)
    {
        var attributes = GetProtoAttributes(test);

        // Execute async post-test hooks, dispose the context scope, and clear ambient state.
        ProtoTestAssembly.Host
            .CompleteTestAsync(attributes)
            .GetAwaiter()
            .GetResult();
    }

    private static List<ProtoAttribute> GetProtoAttributes(ITest test)
    {
        var attributes = new List<ProtoAttribute>();

        // 1. Resolve class-level attributes
        if (test.Method?.MethodInfo.DeclaringType != null)
        {
            attributes.AddRange(
                test.Method.MethodInfo.DeclaringType
                    .GetCustomAttributes(true)
                    .OfType<ProtoAttribute>()
            );
        }

        // 2. Resolve method-level attributes
        if (test.Method?.MethodInfo != null)
        {
            attributes.AddRange(
                test.Method.MethodInfo
                    .GetCustomAttributes(true)
                    .OfType<ProtoAttribute>()
            );
        }

        return attributes;
    }
}