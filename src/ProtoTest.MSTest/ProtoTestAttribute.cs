namespace ProtoTest.MSTest;

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;

/// <summary>
/// Custom MSTest method attribute that manages the ProtoTest execution context lifecycle asynchronously.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProtoTestAttribute([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1) : TestMethodAttribute(callerFilePath, callerLineNumber)
{
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        var methodInfo = testMethod.MethodInfo;
        var attributes = GetProtoAttributes(methodInfo);
        var testId = ProtoTestIdGenerator.Generate(methodInfo);

        // 1. Initialize context for the current test
        ProtoTestAssembly.Host.BeginTestContext(methodInfo.Name, testId, methodInfo);

        try
        {
            // 2. Execute async pre-test hooks & attributes
            await ProtoTestAssembly.Host.ExecuteBeforeHooksAsync(attributes);

            // 3. Run the actual MSTest execution pipeline
            var results = await base.ExecuteAsync(testMethod);

            // 4. Execute async post-test hooks & attributes
            await ProtoTestAssembly.Host.ExecuteAfterHooksAsync(attributes);

            return results;
        }
        finally
        {
            // 5. Clean up context
            ProtoTestAssembly.Host.EndTestContext();
        }
    }

    private static List<ProtoAttribute> GetProtoAttributes(MethodInfo methodInfo)
    {
        var attributes = new List<ProtoAttribute>();

        if (methodInfo.DeclaringType != null)
        {
            attributes.AddRange(
                methodInfo.DeclaringType
                    .GetCustomAttributes(true)
                    .OfType<ProtoAttribute>()
            );
        }

        attributes.AddRange(
            methodInfo
                .GetCustomAttributes(true)
                .OfType<ProtoAttribute>()
        );

        return attributes;
    }
}