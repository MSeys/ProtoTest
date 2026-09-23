namespace ProtoTest.Sql.Tests;

using NUnit.Framework;
using ProtoTest.Sql;

[TestFixture]
public sealed class SqlHookOrderTests
{
    [Test]
    public void SetupHooks_ShouldOpenTheConnectionBeforeAnyEnlistment()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SqlHookOrders.Connection, Is.EqualTo(-1_000));
            Assert.That(SqlHookOrders.Enlistment, Is.EqualTo(-999));
            Assert.That(
                SqlHookOrders.Connection,
                Is.LessThan(SqlHookOrders.Enlistment),
                "setup hooks run in ascending order, so the connection must be prepared before enlistment");
        });
    }
}
