namespace ProtoTest.Analyzers;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// The framework vocabulary the analyzers match by metadata name. An analyzer cannot reference the
/// packages it inspects - it must load into any compiler and stay valid across ProtoTest versions -
/// so the names below are the contract, and the tests pin them against the shipped attributes.
/// </summary>
internal static class ProtoTestVocabulary
{
    /// <summary>Test attributes that start the ProtoTest lifecycle in each runner adapter.</summary>
    public static readonly ImmutableHashSet<string> LifecycleAttributes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ProtoTest.MSTest.ProtoTestAttribute",
        "ProtoTest.NUnit.ProtoTestAttribute",
        "ProtoTest.Xunit.ProtoTestFactAttribute",
        "ProtoTest.Xunit.ProtoTestTheoryAttribute",
        "ProtoTest.Xunit3.ProtoTestFactAttribute",
        "ProtoTest.Xunit3.ProtoTestTheoryAttribute");

    /// <summary>
    /// The runner's own test attributes that every ProtoTest lifecycle attribute derives from. TUnit
    /// is deliberately absent: <c>ProtoTestExecutor</c> wraps every TUnit <c>[Test]</c> in the
    /// lifecycle, so its attribute is not a plain registration.
    /// </summary>
    public static readonly ImmutableHashSet<string> PlainTestAttributes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "NUnit.Framework.TestAttribute",
        "Xunit.FactAttribute",
        "Xunit.TheoryAttribute");

    /// <summary>
    /// Assembly attributes that run a runner's plain tests inside the lifecycle, with the plain
    /// attributes each one wraps. MSTest and xUnit v2 have no such mode.
    /// </summary>
    public static readonly ImmutableDictionary<string, ImmutableHashSet<string>> AutoWrapAttributes =
        ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            new[]
            {
                new KeyValuePair<string, ImmutableHashSet<string>>(
                    "ProtoTest.NUnit.ProtoTestAutoWrapAttribute",
                    ImmutableHashSet.Create(StringComparer.Ordinal, "NUnit.Framework.TestAttribute")),
                new KeyValuePair<string, ImmutableHashSet<string>>(
                    "ProtoTest.Xunit3.ProtoTestAutoWrapAttribute",
                    ImmutableHashSet.Create(StringComparer.Ordinal, "Xunit.FactAttribute", "Xunit.TheoryAttribute"))
            });

    /// <summary>TUnit's test attribute; its tests run in the lifecycle when the ProtoTest executor is registered.</summary>
    public const string TUnitTestAttribute = "TUnit.Core.TestAttribute";

    /// <summary>The TUnit executor that wraps every test it runs in the lifecycle.</summary>
    public const string TUnitExecutor = "ProtoTest.TUnit.ProtoTestExecutor";

    /// <summary>The one static gateway the ambient execution context is read through.</summary>
    public const string ContextType = "ProtoTest.Core.Proto";

    /// <summary>The property that resolves the ambient execution context.</summary>
    public const string ContextProperty = "Context";
}
