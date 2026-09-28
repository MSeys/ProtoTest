namespace ProtoTest.Traces.Tests;

/// <summary>A throwaway folder that can hold several runs, deleted when a test is done with it.</summary>
internal static class TraceFolders
{
    /// <summary>Creates a unique empty folder under the temp directory.</summary>
    public static string Create() => Directory.CreateTempSubdirectory("prototest-traces-").FullName;

    /// <summary>Deletes a folder and everything in it, ignoring a locked file.</summary>
    public static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
