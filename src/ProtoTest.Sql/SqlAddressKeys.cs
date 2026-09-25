namespace ProtoTest.Sql;

/// <summary>
/// The code-declared configuration keys that can provide the SQL connection:
/// <c>sql =&gt; sql.AddressKeys.Add("ConnectionStrings:Orders")</c>. The set is a code API, not a bound
/// option: the capability decision is made when the host is built, before options bind, so a key that
/// only configuration knows cannot promise a connection the build already decided against.
/// </summary>
public sealed class SqlAddressKeys
{
    private readonly List<string> _keys = [];

    /// <summary>Gets the number of declared keys.</summary>
    public int Count => _keys.Count;

    /// <summary>Gets the declared key at <paramref name="index"/>, in declaration order.</summary>
    public string this[int index] => _keys[index];

    /// <summary>
    /// Declares one or more configuration keys that can provide the connection. A key repeated in the
    /// same set is ignored; declaration order is kept.
    /// </summary>
    /// <exception cref="ArgumentException">A key is null, empty or whitespace.</exception>
    public SqlAddressKeys Add(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (!_keys.Contains(key, StringComparer.Ordinal))
            {
                _keys.Add(key);
            }
        }

        return this;
    }

    /// <summary>Returns a snapshot of the declared keys in declaration order.</summary>
    public string[] ToArray() => [.. _keys];
}
