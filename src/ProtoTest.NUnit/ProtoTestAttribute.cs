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
        var attributes = ProtoAttributeResolver.Resolve(test.Method!.MethodInfo);
        ProtoTestAssembly.Host
            .StartTestAsync(test.FullName, test.Method!.MethodInfo, attributes, NUnitAttachmentPublisher.Instance)
            .GetAwaiter()
            .GetResult();
    }

    public void AfterTest(ITest test)
    {
        // Execute async post-test hooks, dispose the context scope, and clear ambient state.
        ProtoTestAssembly.Host
            .CompleteTestAsync()
            .GetAwaiter()
            .GetResult();
    }

}
