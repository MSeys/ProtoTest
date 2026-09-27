namespace ProtoTest.NUnit;

using global::NUnit.Framework.Interfaces;
using global::NUnit.Framework.Internal.Commands;

/// <summary>
/// Opt-in low-ceremony mode: apply this attribute at assembly level and every plain <c>[Test]</c> in
/// the assembly runs inside a ProtoTest execution context, exactly like a test carrying
/// <see cref="ProtoTestAttribute"/>. NUnit applies the nearest <c>IWrapSetUpTearDown</c> attribute
/// (method, then fixture, then assembly), so a test that already carries <see cref="ProtoTestAttribute"/>
/// keeps its own wrapper and is never wrapped twice.
/// </summary>
/// <example>
/// <code>
/// [assembly: ProtoTestAutoWrap]
///
/// [Test]
/// public async Task PlainTestsGetAContext() =&gt; Assert.That(Proto.Context, Is.Not.Null);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ProtoTestAutoWrapAttribute : Attribute, IWrapSetUpTearDown
{
    /// <summary>Wraps the test with the shared ProtoTest lifecycle command.</summary>
    public TestCommand Wrap(TestCommand command) => ProtoTestAttribute.WrapCommand(command);
}
