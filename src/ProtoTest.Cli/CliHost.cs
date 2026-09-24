namespace ProtoTest.Cli;

using ProtoTest.Traces;

/// <summary>
/// The command-line surface, separated from the entry point so tests can drive it with their own writers.
/// </summary>
public static class CliHost
{
    /// <summary>Runs one command. Returns the process exit code.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 3
            && string.Equals(args[0], "trace", StringComparison.Ordinal)
            && string.Equals(args[1], "summary", StringComparison.Ordinal))
        {
            return Summary(args[2], output, error);
        }

        WriteUsage(error);
        return 1;
    }

    private static int Summary(string path, TextWriter output, TextWriter error)
    {
        if (!File.Exists(path))
        {
            error.WriteLine($"Trace file not found: {path}");
            return 1;
        }

        try
        {
            ProtoTraceSummaryText.Write(ProtoTraceArchive.Open(path), output);
            return 0;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Could not read '{path}': {exception.Message}");
            return 1;
        }
    }

    private static void WriteUsage(TextWriter writer)
        => writer.WriteLine("usage: prototest trace summary <file.prototrace>");
}
