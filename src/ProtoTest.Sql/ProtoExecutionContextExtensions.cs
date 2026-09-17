namespace ProtoTest.Sql;

using System.Data.Common;
using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>Gets the database session owned by this test execution.</summary>
    public static ProtoSqlSession SqlSession(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Service<ProtoSqlSession>();
    }

    /// <summary>Gets the database connection owned by this test execution.</summary>
    public static DbConnection SqlConnection(this ProtoExecutionContext context)
        => SqlSession(context).Connection;

    /// <summary>Gets the test's transaction, or <see langword="null"/> when isolation is <see cref="SqlIsolation.None"/>.</summary>
    public static DbTransaction? SqlTransaction(this ProtoExecutionContext context)
        => SqlSession(context).Transaction;
}
