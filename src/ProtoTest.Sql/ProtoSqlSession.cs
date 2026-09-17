namespace ProtoTest.Sql;

using System.Data.Common;

/// <summary>
/// The per-test database session: the connection ProtoTest owns and, when the isolation strategy needs
/// one, the transaction every access technology must enlist in.
/// </summary>
public sealed class ProtoSqlSession
{
    internal ProtoSqlSession(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Connection = connection;
    }

    /// <summary>Gets the connection owned by this test.</summary>
    public DbConnection Connection { get; }

    /// <summary>Gets the test's transaction, or <see langword="null"/> when isolation is <see cref="SqlIsolation.None"/>.</summary>
    public DbTransaction? Transaction { get; internal set; }
}
