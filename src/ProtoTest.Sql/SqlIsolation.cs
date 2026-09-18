namespace ProtoTest.Sql;

/// <summary>How a test is isolated from the database's committed state.</summary>
public enum SqlIsolation
{
    /// <summary>
    /// The test runs on a connection with an open transaction that is rolled back when the test ends,
    /// so everything written through that connection never persists. Writes made through a different
    /// connection - for example by an application that owns its own - are not covered, so when the host
    /// registers applications, each one must be declared with
    /// <see cref="ProtoSqlOptions.ShareConnectionWith(string[])"/> (or this strategy is rejected at run start).
    /// </summary>
    Transaction,

    /// <summary>
    /// No isolation. Writes persist, so provisioners are responsible for releasing what they created.
    /// </summary>
    None
}
