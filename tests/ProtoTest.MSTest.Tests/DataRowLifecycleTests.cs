namespace ProtoTest.MSTest.Tests;

using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;

/// <summary>
/// Characterization for MSTest data rows. MSTest invokes the test method
/// attribute once per data row, so every row gets its own ProtoTest lifecycle under the same method-level
/// name. The multi-row aggregation in <c>ToProtoTestResult</c> is unreachable.
/// </summary>
[TestClass]
public sealed class DataRowLifecycleTests
{
    private static readonly ConcurrentDictionary<string, byte> SeenLifetimes = new(StringComparer.Ordinal);

    [ProtoTest]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void Rows_ShouldCharacterizeOneLifecyclePerRow(int value)
    {
        var context = Proto.Context;

        Assert.IsTrue(
            SeenLifetimes.TryAdd(context.TestId, 0),
            $"Row {value} reused lifetime '{context.TestId}'; MSTest must invoke the attribute once per row.");
        Assert.IsTrue(
            context.TestName.StartsWith(
                "ProtoTest.MSTest.Tests.DataRowLifecycleTests.Rows_ShouldCharacterizeOneLifecyclePerRow",
                StringComparison.Ordinal),
            $"Unexpected trace name '{context.TestName}'.");
        Assert.IsTrue(
            context.TestName.Contains($"[{value}]", StringComparison.Ordinal),
            $"The row's arguments make its trace name distinct; got '{context.TestName}'.");
    }
}
