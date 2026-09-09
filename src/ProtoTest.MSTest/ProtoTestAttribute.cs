namespace ProtoTest.MSTest;

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
        var attributes = ProtoAttributeResolver.Resolve(methodInfo);
        try
        {
            await ProtoTestAssembly.Host.StartTestAsync(methodInfo.Name, methodInfo, attributes);

            // 3. Run the actual MSTest execution pipeline
            var results = await base.ExecuteAsync(testMethod);

            return results;
        }
        finally
        {
            // Complete the lifecycle even when setup or test execution fails.
            await ProtoTestAssembly.Host.CompleteTestAsync();
        }
    }

}
