namespace ProtoTest.Sql;

/// <summary>Configures how ProtoTest owns and isolates a database connection during a test.</summary>
public sealed class ProtoSqlOptions
{
    private readonly HashSet<string> _sharedWith = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the isolation applied to each test. Defaults to <see cref="SqlIsolation.Transaction"/>.</summary>
    public SqlIsolation Isolation { get; set; } = SqlIsolation.Transaction;

    /// <summary>Gets the applications declared as using the test's connection.</summary>
    public IReadOnlyCollection<string> SharedWith => _sharedWith;

    /// <summary>
    /// Declares that an application uses the connection ProtoTest owns, so
    /// <see cref="SqlIsolation.Transaction"/> rolls back the application's writes too. When the host
    /// registers applications, every one of them must be declared for the transaction strategy to run.
    /// </summary>
    public ProtoSqlOptions ShareConnectionWith(params string[] applicationNames)
    {
        ArgumentNullException.ThrowIfNull(applicationNames);
        foreach (var applicationName in applicationNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
            _sharedWith.Add(applicationName);
        }

        return this;
    }

    /// <summary>Returns whether an application was declared as sharing the test's connection.</summary>
    public bool SharesConnectionWith(string applicationName)
        => applicationName is not null && _sharedWith.Contains(applicationName);
}
