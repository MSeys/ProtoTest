namespace ProtoTest.Core.Tests;

using NUnit.Framework;
using System.Reflection;

[TestFixture]
public sealed class ProtoTestIdTests
{
    [Test]
    public void Parse_ShouldPreserveLeadingZeroesAndExposeNumericValue()
    {
        var id = ProtoTestId.Parse("000042");

        Assert.Multiple(() =>
        {
            Assert.That(id.Number, Is.EqualTo(42));
            Assert.That(id.Value, Is.EqualTo("000042"));
            Assert.That(id.ToString(), Is.EqualTo("000042"));
        });
    }

    [TestCase("12A3")]
    [TestCase("1234567890123456789")]
    public void Parse_ShouldRejectNonNumericOrOversizedValues(string value)
    {
        Assert.Throws<FormatException>(() => ProtoTestId.Parse(value));
    }

    [Test]
    public void Parse_ShouldRejectEmptyValue()
    {
        Assert.Throws<ArgumentException>(() => ProtoTestId.Parse(""));
    }

    [Test]
    public async Task Host_ShouldGenerateConfiguredUniqueNumericIds()
    {
        var builder = new ProtoHostBuilder()
            .ConfigureTestIds(options =>
            {
                options.RunPrefix = 123456;
                options.SequenceDigits = 3;
            });
        await using var host = builder.Build();
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        var first = await host.StartTestAsync("First", method);
        await host.CompleteTestAsync();
        var second = await host.StartTestAsync("Second", method);
        await host.CompleteTestAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.TestId, Is.EqualTo("123456001"));
            Assert.That(first.TestNumber, Is.EqualTo(123456001));
            Assert.That(second.TestId, Is.EqualTo("123456002"));
        });
    }
}
