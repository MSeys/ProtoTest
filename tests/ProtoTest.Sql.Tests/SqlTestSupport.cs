namespace ProtoTest.Sql.Tests;

using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

/// <summary>
/// An in-memory SQLite database kept alive for one test by its keeper connection. Pooling is off so the
/// keeper alone decides when the database disappears; the test's connection and the context's share its
/// cache, so both see the same schema and data.
/// </summary>
internal sealed class SqliteKeeper : IDisposable
{
    private readonly SqliteConnection _keeper;

    public SqliteKeeper(string name)
    {
        ConnectionString = $"Data Source=file:{name};Mode=Memory;Cache=Shared;Pooling=False";
        _keeper = new SqliteConnection(ConnectionString);
        _keeper.Open();
        Execute(_keeper, "CREATE TABLE IF NOT EXISTS Widgets (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);");
    }

    public string ConnectionString { get; }

    /// <summary>The keeper connection that owns the database's lifetime.</summary>
    public DbConnection Connection => _keeper;

    public static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static int CountWidgets(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Widgets;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose() => _keeper.Dispose();
}

/// <summary>
/// A connection that fails where a test needs it to: on open, on begin, or when rolling back. Disposal
/// and the started transaction stay observable, so the ownership on a failure path can be asserted.
/// </summary>
internal sealed class FailingDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public bool FailOpen { get; init; }

    public bool FailBegin { get; init; }

    public bool FailRollback { get; init; }

    public bool IsDisposed { get; private set; }

    public FailingDbTransaction? Transaction { get; private set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => "fake";

    public override string DataSource => "fake";

    public override string ServerVersion => "1";

    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close() => _state = ConnectionState.Closed;

    public override void Open() => _state = ConnectionState.Open;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        if (FailOpen)
        {
            throw new InvalidOperationException("open failed");
        }

        _state = ConnectionState.Open;
        return Task.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (FailBegin)
        {
            throw new InvalidOperationException("begin failed");
        }

        Transaction = new FailingDbTransaction(this, isolationLevel, FailRollback);
        return Transaction;
    }

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        _state = ConnectionState.Closed;
        base.Dispose(disposing);
    }
}

internal sealed class FailingDbTransaction(
    DbConnection connection,
    IsolationLevel isolationLevel,
    bool failRollback) : DbTransaction
{
    public bool Disposed { get; private set; }

    public override IsolationLevel IsolationLevel { get; } = isolationLevel;

    protected override DbConnection DbConnection { get; } = connection;

    public override void Rollback()
    {
        if (failRollback)
        {
            throw new InvalidOperationException("rollback failed");
        }
    }

    public override void Commit()
    {
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
