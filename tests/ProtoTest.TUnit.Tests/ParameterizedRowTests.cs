namespace ProtoTest.TUnit.Tests;

using ProtoTest.Core;

public sealed class ParameterizedRowTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Rows_ShouldRecordTheirOwnTraceNames(int row)
    {
        // TUnit names every row by the method alone, so parallel rows would be
        // indistinguishable in the trace. The adapter now composes the same row form MSTest records;
        // the rows run in parallel, and each asserts its own name and its own trace record.
        var method = typeof(ParameterizedRowTests).GetMethod(nameof(Rows_ShouldRecordTheirOwnTraceNames))!;
        var expected = ProtoTestName.ForRow(method, [row]);

        await Assert.That(Proto.Context.TestName).IsEqualTo(expected);
        await Assert.That(ProtoTestAssembly.Host.Trace.Snapshot().Tests.Count(test => test.Name == expected)).IsEqualTo(1);
    }
}
