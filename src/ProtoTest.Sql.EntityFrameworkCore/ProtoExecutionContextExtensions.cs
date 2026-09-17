namespace ProtoTest.Sql.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>Gets the database context owned by this test execution.</summary>
    public static TContext Sql<TContext>(this ProtoExecutionContext context)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Service<TContext>();
    }
}
