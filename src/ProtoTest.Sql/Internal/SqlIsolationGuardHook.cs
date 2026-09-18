namespace ProtoTest.Sql.Internal;

using ProtoTest.Core;

/// <summary>
/// Rejects a run that asks for <see cref="SqlIsolation.Transaction"/> while applications are registered
/// that were not declared as sharing the test's connection. The transaction only covers that connection,
/// so an undeclared application would keep writing outside it and appear to be isolated when it is not.
/// Applications hosted without being declared through <c>AddApplication</c> cannot be detected here.
/// </summary>
internal sealed class SqlIsolationGuardHook(
    ProtoSqlOptions options,
    IEnumerable<ProtoApplicationClients> applications) : IProtoRunHook
{
    public int Order => -500;

    public Task BeforeRunAsync(CancellationToken cancellationToken = default)
    {
        if (options.Isolation != SqlIsolation.Transaction)
        {
            return Task.CompletedTask;
        }

        var undeclared = applications
            .Select(application => application.ApplicationName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !options.SharesConnectionWith(name))
            .ToArray();

        if (undeclared.Length == 0)
        {
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"SqlIsolation.Transaction rolls back only writes made through the connection ProtoTest owns, " +
            $"but the registered application(s) {string.Join(", ", undeclared.Select(name => $"'{name}'"))} " +
            $"were not declared as sharing it. Declare every application that uses that connection with " +
            $"sql.ShareConnectionWith(\"name\"), or use SqlIsolation.None and release what the test creates.");
    }
}
