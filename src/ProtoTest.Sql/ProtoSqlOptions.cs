namespace ProtoTest.Sql;

/// <summary>Configures how ProtoTest owns and isolates a database connection during a test.</summary>
public sealed class ProtoSqlOptions
{
    /// <summary>Gets or sets the isolation applied to each test. Defaults to <see cref="SqlIsolation.TransactionPerTest"/>.</summary>
    public SqlIsolation Isolation { get; set; } = SqlIsolation.TransactionPerTest;
}
