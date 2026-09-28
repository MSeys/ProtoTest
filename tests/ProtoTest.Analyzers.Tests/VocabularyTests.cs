namespace ProtoTest.Analyzers.Tests;

/// <summary>
/// Pins the exact metadata names the analyzers match, so a rename or a typo fails here instead of
/// silently stopping a rule. NUnit and xUnit v3 resolve against the shipped attribute types. The
/// MSTest and xUnit v2 adapter assemblies cannot load in one fixture next to those two, so their names
/// are asserted literally and carried by the <see cref="FixtureStubs"/> fixtures. TUnit is pinned
/// absent: its executor gives every test the lifecycle.
/// </summary>
public sealed class VocabularyTests
{
    [Test]
    public void LifecycleAttributes_ShouldPinTheShippedAdapterAttributeNames()
    {
        Assert.That(
            ProtoTestVocabulary.LifecycleAttributes,
            Is.EquivalentTo(new[]
            {
                typeof(ProtoTest.NUnit.ProtoTestAttribute).FullName!,
                "ProtoTest.MSTest.ProtoTestAttribute",
                "ProtoTest.Xunit.ProtoTestFactAttribute",
                "ProtoTest.Xunit.ProtoTestTheoryAttribute",
                typeof(ProtoTest.Xunit3.ProtoTestFactAttribute).FullName!,
                typeof(ProtoTest.Xunit3.ProtoTestTheoryAttribute).FullName!,
            }));
    }

    [Test]
    public void PlainTestAttributes_ShouldPinTheShippedRunnerAttributeNames()
    {
        Assert.That(
            ProtoTestVocabulary.PlainTestAttributes,
            Is.EquivalentTo(new[]
            {
                typeof(global::NUnit.Framework.TestAttribute).FullName!,
                // xUnit v2 and v3 ship the same metadata names, so the v3 assembly pins both.
                typeof(Xunit.FactAttribute).FullName!,
                typeof(Xunit.TheoryAttribute).FullName!,
                "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
            }));
    }

    [Test]
    public void TUnitTestAttribute_ShouldStayOutsideTheVocabulary()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ProtoTestVocabulary.LifecycleAttributes, Does.Not.Contain("TUnit.Core.TestAttribute"));
            Assert.That(ProtoTestVocabulary.PlainTestAttributes, Does.Not.Contain("TUnit.Core.TestAttribute"));
        }
    }

    [Test]
    public void SheetVocabulary_ShouldPinTheShippedAttributeNames()
    {
        var sheetAttribute = ProtoTestVocabulary.SheetAttribute;
        var columnAttribute = ProtoTestVocabulary.ColumnAttribute;
        var sheetKindType = ProtoTestVocabulary.SheetKindType;
        var sheetKindKeyValue = ProtoTestVocabulary.SheetKindKeyValue;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sheetAttribute, Is.EqualTo(typeof(ProtoTest.Sheets.SheetAttribute).FullName));
            Assert.That(columnAttribute, Is.EqualTo(typeof(ProtoTest.Sheets.ColumnAttribute).FullName));
            Assert.That(sheetKindType, Is.EqualTo(typeof(ProtoTest.Sheets.ProtoSheetKind).FullName));
            Assert.That(sheetKindKeyValue, Is.EqualTo(nameof(ProtoTest.Sheets.ProtoSheetKind.KeyValue)));
        }
    }
}
