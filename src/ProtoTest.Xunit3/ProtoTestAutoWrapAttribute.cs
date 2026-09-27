namespace ProtoTest.Xunit3;

using System.Reflection;
using Xunit.v3;

/// <summary>
/// Opt-in low-ceremony mode: apply this attribute at assembly level and every xUnit v3 test in the
/// assembly runs inside a ProtoTest execution context, exactly like a test carrying
/// <see cref="ProtoTestFactAttribute"/>. A test that already carries a ProtoTest attribute keeps its
/// own lifecycle and is not wrapped twice.
/// </summary>
/// <example>
/// <code>
/// [assembly: ProtoTestAutoWrap]
///
/// [Fact]
/// public async Task PlainFactsGetAContext() =&gt; Assert.NotNull(Proto.Context);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ProtoTestAutoWrapAttribute : Attribute, IBeforeAfterTestAttribute
{
    void IBeforeAfterTestAttribute.Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (HasProtoTestAttribute(methodUnderTest))
        {
            return;
        }

        ProtoTestLifecycleHandler.Before(methodUnderTest, test);
    }

    void IBeforeAfterTestAttribute.After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (HasProtoTestAttribute(methodUnderTest))
        {
            return;
        }

        ProtoTestLifecycleHandler.After(test);
    }

    /// <summary>
    /// The attributes that already own the test's lifecycle. Their own before/after handler runs, so
    /// the assembly-level wrapper must step aside or the test would start two scopes.
    /// </summary>
    private static bool HasProtoTestAttribute(MethodInfo method)
        => method.GetCustomAttributes<ProtoTestFactAttribute>(inherit: true).Any()
            || method.GetCustomAttributes<ProtoTestTheoryAttribute>(inherit: true).Any();
}
