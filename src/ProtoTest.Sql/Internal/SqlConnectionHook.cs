namespace ProtoTest.Sql.Internal;

using ProtoTest.Core;

/// <summary>
/// Owns the test's database connection as a resource and asks the session to open it. The resource is
/// registered before the connection is opened, so an open or begin failure still owns what it created.
/// </summary>
internal sealed class SqlConnectionHook(SqlOptions options) : IProtoTestHook
{
    public int Order => SqlHookOrders.Connection;

    public async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var session = context.Service<ProtoSqlSession>();

        // The resource is registered before the connection is opened: an open or begin failure must
        // still own what it created, so a started transaction is rolled back and the connection is
        // disposed during teardown instead of escaping the test's ownership.
        var sharing = options.SharedWith.Count == 0
            ? string.Empty
            : $" · shared with {string.Join(", ", options.SharedWith)}";
        context.RegisterResource(new ProtoResource(
            "database:connection",
            "database",
            $"{session.Connection.GetType().Name} · {options.Isolation}{sharing}",
            session.ReleaseAsync));

        await session.StartAsync(context);
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}
