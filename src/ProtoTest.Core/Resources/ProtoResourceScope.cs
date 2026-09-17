namespace ProtoTest.Core;

/// <summary>How long an owned resource lives.</summary>
public enum ProtoResourceScope
{
    /// <summary>Owned by one test and released when that test ends, before its clients are disposed.</summary>
    Test,

    /// <summary>
    /// Owned by the whole run and released when the host is disposed, after the run has stopped and
    /// the reports are written - for infrastructure a suite starts once, such as a database container.
    /// </summary>
    Run
}
