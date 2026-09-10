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
        var attachmentPublisher = new MSTestAttachmentPublisher();
        TestResult[]? results = null;
        var lifecycleStarted = false;
        try
        {
            await ProtoTestAssembly.Host.StartTestAsync(
                methodInfo.Name, methodInfo, attributes, attachmentPublisher);
            lifecycleStarted = true;

            results = await base.ExecuteAsync(testMethod);

            return results;
        }
        finally
        {
            try
            {
                if (lifecycleStarted)
                {
                    await ProtoTestAssembly.Host.CompleteTestAsync();
                }
            }
            finally
            {
                if (results is not null && attachmentPublisher.Files.Count > 0)
                {
                    foreach (var result in results)
                    {
                        result.ResultFiles = [.. result.ResultFiles ?? [], .. attachmentPublisher.Files];
                    }
                }
            }
        }
    }

}
