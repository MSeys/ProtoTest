namespace ProtoTest.Sql;

using System.Data.Common;
using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Gets the database session owned by this test execution. Throws naming the missing keys when the
    /// integration is inert: its <see cref="SqlOptions.AddressKeys"/> are declared and none is provided.
    /// </summary>
    public static ProtoSqlSession Sql(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TryService<SqlOptions>() is { } options)
        {
            SqlAddressRule.ThrowIfInert(context, options);
        }

        return context.Service<ProtoSqlSession>();
    }

    /// <summary>
    /// Gets the database session owned by this test execution; the historical name of
    /// <see cref="Sql(ProtoExecutionContext)"/>, kept as a documented alias. Prefer <c>Sql()</c>, which
    /// reads like the other protocol accessors.
    /// </summary>
    public static ProtoSqlSession SqlSession(this ProtoExecutionContext context) => Sql(context);

    /// <summary>Gets the database connection owned by this test execution.</summary>
    public static DbConnection SqlConnection(this ProtoExecutionContext context)
        => Sql(context).Connection;

    /// <summary>Gets the test's transaction, or <see langword="null"/> when isolation is <see cref="SqlIsolation.None"/>.</summary>
    public static DbTransaction? SqlTransaction(this ProtoExecutionContext context)
        => Sql(context).Transaction;
}
