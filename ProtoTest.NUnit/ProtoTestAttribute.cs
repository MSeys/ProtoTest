namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using global::NUnit.Framework.Interfaces;
using ProtoTest.Core;
using System.Reflection;

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

        // 1. Synchronously bind context on NUnit's test execution frame
        ProtoTestAssembly.Host.BeginTestContext(test.FullName, testId);

        // 2. Execute async pre-test hooks & attributes
        ProtoTestAssembly.Host
            .ExecuteBeforeHooksAsync(attributes)
            .GetAwaiter()
            .GetResult();
    }

    public void AfterTest(ITest test)
    {
        var attributes = GetProtoAttributes(test);

        try
        {
            // 1. Execute async post-test hooks & attributes and dispose context scope
            ProtoTestAssembly.Host
                .ExecuteAfterHooksAsync(attributes)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            // 2. Synchronously unbind context from NUnit's test execution frame
            ProtoTestAssembly.Host.EndTestContext();
        }
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