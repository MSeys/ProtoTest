namespace ProtoTest.Sql;

/// <summary>How a test is isolated from the database's committed state.</summary>
public enum SqlIsolation
{
    /// <summary>
    /// The test runs on a connection with an open transaction that is rolled back when the test ends,
    /// so everything written through that connection never persists. Writes made through a different
    /// connection - for example by an application that owns its own - are not covered.
    /// </summary>
    TransactionPerTest,

    /// <summary>
    /// No isolation. Writes persist, so provisioners are responsible for releasing what they created.
    /// </summary>
    None
}
