namespace ProtoTest.Xunit;

using System.Reflection;
using global::Xunit.Sdk;
using ProtoTest.Core;

/// <summary>
/// Legacy xUnit v2 combination attribute for use with <c>[Fact]</c>. It manages the
/// <see cref="ProtoExecutionContext"/> lifecycle, but xUnit v2 records the outcome after
/// <see cref="BeforeAfterTestAttribute"/> hooks finish, so this style can only report <c>Unknown</c>.
/// Prefer <see cref="ProtoTestFactAttribute"/> or <see cref="ProtoTestTheoryAttribute"/>.
/// </summary>
[Obsolete("Prefer [ProtoTestFact] or [ProtoTestTheory]; with [Fact] + [ProtoTest] the recorded outcome is always Unknown.")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ProtoTestAttribute : BeforeAfterTestAttribute
{
    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest)
    {
        var attributes = ProtoAttributeResolver.Resolve(methodUnderTest);
        ProtoTestAssembly.Host
            .StartTestAsync(ProtoTestName.FromMethod(methodUnderTest), methodUnderTest, attributes, Xunit2AttachmentPublisher.Instance)
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest)
    {
        ProtoTestAssembly.Host
            .CompleteTestAsync()
            .GetAwaiter()
            .GetResult();
    }

}
