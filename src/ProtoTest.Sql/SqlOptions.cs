namespace ProtoTest.Sql;

using ProtoTest.Core;

/// <summary>Configures how ProtoTest owns and isolates a database connection during a test.</summary>
public sealed class SqlOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Sql";

    private readonly HashSet<string> _sharedWith = new(StringComparer.OrdinalIgnoreCase);

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>Gets or sets the isolation applied to each test. Defaults to <see cref="SqlIsolation.Transaction"/>.</summary>
    public SqlIsolation Isolation { get; set; } = SqlIsolation.Transaction;

    /// <summary>
    /// The configuration keys that can provide the connection. When at least one is declared and none
    /// is provided - no configured value and no registered infrastructure piece declares it - the SQL
    /// capability is absent and the integration is inert until a key exists: the connection is not
    /// opened during setup, and <see cref="ProtoExecutionContextExtensions.SqlSession(ProtoExecutionContext)"/>
    /// and its siblings throw naming the keys and the
    /// <c>[RequiresCapability(ProtoCapabilityKinds.Store)]</c> gate. Empty by default: the capability
    /// stays unconditional and the factory owns the address.
    /// </summary>
    /// <remarks>
    /// Declare the keys in the <c>AddSql</c> callback, for example
    /// <c>sql =&gt; sql.AddressKeys.Add("ConnectionStrings:Orders")</c>. The set is a code API and is
    /// never bound from configuration, because the capability decision is made when the host is built,
    /// before options bind. <c>AddEntityFrameworkCore</c> declares its store capability over the same
    /// keys when it is called after <c>AddSql</c>.
    /// </remarks>
    public SqlAddressKeys AddressKeys { get; } = new();

    /// <summary>Gets the applications declared as using the test's connection.</summary>
    public IReadOnlyCollection<string> SharedWith => _sharedWith;

    /// <summary>
    /// Gets or sets the applications declared as sharing the test's connection, for configuration
    /// binding (<c>ProtoTest:Sql:SharedWithApplications</c>). Prefer <see cref="ShareConnectionWith"/>.
    /// </summary>
    public List<string> SharedWithApplications
    {
        get => [.. _sharedWith];
        set
        {
            _sharedWith.Clear();
            if (value is null)
            {
                return;
            }

            ShareConnectionWith([.. value.Where(name => !string.IsNullOrWhiteSpace(name))]);
        }
    }

    /// <summary>
    /// Declares that an application uses the connection ProtoTest owns, so
    /// <see cref="SqlIsolation.Transaction"/> rolls back the application's writes too. When the host
    /// registers applications, every one of them must be declared for the transaction strategy to run.
    /// </summary>
    public SqlOptions ShareConnectionWith(params string[] applicationNames)
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
