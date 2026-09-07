namespace ProtoTest.Xunit3;

using System.Reflection;
using Xunit;
using Xunit.v3;

/// <summary>
/// Marks a method as a ProtoTest Theory in xUnit v3 and manages its context lifecycle.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProtoTestTheoryAttribute : TheoryAttribute, IBeforeAfterTestAttribute
{
    /// <inheritdoc />
    public void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        ProtoTestLifecycleHandler.Before(methodUnderTest, test);
    }

    /// <inheritdoc />
    public void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        ProtoTestLifecycleHandler.After(methodUnderTest, test);
    }
}