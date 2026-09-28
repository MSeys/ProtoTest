namespace ProtoTest.Mcp.Tests;

using ProtoTest.Traces;

/// <summary>A throwaway project folder that holds the committed fixtures and cleans itself up.</summary>
internal sealed class TestProjectFolder : IDisposable
{
    public TestProjectFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"prototest-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TestResults);
    }

    public string Path { get; }

    public string TestResults => System.IO.Path.Combine(Path, "TestResults");

    /// <summary>The output path of one committed fixture archive.</summary>
    public static string FixturePath(string name)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{name}.prototrace");

    /// <summary>Copies a committed fixture into this folder's TestResults and returns the copy's path.</summary>
    public string AddFixture(string name)
    {
        var target = System.IO.Path.Combine(TestResults, $"{name}.prototrace");
        File.Copy(FixturePath(name), target);
        return target;
    }

    /// <summary>Reads the run id of a trace file.</summary>
    public static string RunId(string traceFile) => ProtoTraceArchive.Open(traceFile).RunId;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
