namespace ProtoTest.Core.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ProtoAssertionTests
{
    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    public void IsSatisfied_ShouldInvertOnlyWhenNegated(bool holds, bool negated, bool expected)
        => Assert.That(ProtoAssertion.IsSatisfied(holds, negated), Is.EqualTo(expected));

    [Test]
    public void IsSatisfied_ShouldDefaultToThePositiveForm()
        => Assert.Multiple(() =>
        {
            Assert.That(ProtoAssertion.IsSatisfied(true), Is.True);
            Assert.That(ProtoAssertion.IsSatisfied(false), Is.False);
        });

    [Test]
    public void Describe_ShouldPrefixOnlyWhenNegated()
        => Assert.Multiple(() =>
        {
            Assert.That(ProtoAssertion.Describe("be visible"), Is.EqualTo("be visible"));
            Assert.That(ProtoAssertion.Describe("be visible", negated: true), Is.EqualTo("not be visible"));
        });

    [TestCase("")]
    [TestCase("   ")]
    public void Describe_ShouldRejectAnEmptyExpectation(string expectation)
        => Assert.Throws<ArgumentException>(() => ProtoAssertion.Describe(expectation));
}
