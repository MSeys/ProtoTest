namespace ProtoTest.Sql;

using System.Data.Common;
using ProtoTest.Core;
using ProtoTest.Sql.Internal;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Gets the database session owned by this test execution. Throws naming the missing keys when the
    /// integration is inert: its <see cref="SqlOptions.AddressKeys"/> are declared and none is provided.
    /// </summary>
    public static ProtoSqlSession SqlSession(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TryService<SqlOptions>() is { } options)
        {
            SqlAddressRule.ThrowIfInert(context, options);
        }

        return context.Service<ProtoSqlSession>();
    }

    /// <summary>Gets the database connection owned by this test execution.</summary>
    public static DbConnection SqlConnection(this ProtoExecutionContext context)
        => SqlSession(context).Connection;

    /// <summary>Gets the test's transaction, or <see langword="null"/> when isolation is <see cref="SqlIsolation.None"/>.</summary>
    public static DbTransaction? SqlTransaction(this ProtoExecutionContext context)
        => SqlSession(context).Transaction;
}
