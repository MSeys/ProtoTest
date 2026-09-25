namespace ProtoTest.TestSupport;

/// <summary>
/// A unique <c>.prototrace</c> file under the temp directory, deleted when disposed - the one home
/// for the per-test trace files suites used to create and delete by hand (audit TST-2). Dispose runs
/// whether the test passed or threw, so a failing test cleans up like the old <c>finally</c> did.
/// </summary>
public sealed class TemporaryTrace : IDisposable
{
    public TemporaryTrace(string prefix)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"prototest-{prefix}-{Guid.NewGuid():N}.prototrace");
    }

    /// <summary>The file path to give the tracing options and to read back after the run.</summary>
    public string Path { get; }

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
