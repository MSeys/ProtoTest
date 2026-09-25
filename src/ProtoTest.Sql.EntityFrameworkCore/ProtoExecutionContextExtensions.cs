namespace ProtoTest.Sql.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using ProtoTest.Core;
using ProtoTest.Sql;
using ProtoTest.Sql.Internal;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Gets the database context owned by this test execution. Throws naming the missing keys when the
    /// SQL integration is inert: its <see cref="SqlOptions.AddressKeys"/> are declared and none is
    /// provided, so no context can use the connection ProtoTest owns.
    /// </summary>
    public static TContext Sql<TContext>(this ProtoExecutionContext context)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TryService<SqlOptions>() is { } options)
        {
            SqlAddressRule.ThrowIfInert(context, options);
        }

        return context.Service<TContext>();
    }
}
