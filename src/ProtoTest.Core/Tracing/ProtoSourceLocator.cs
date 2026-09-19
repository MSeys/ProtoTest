namespace ProtoTest.Core;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Finds where in the suite's own code an operation started: the first stack frame that belongs neither to
/// ProtoTest nor to the runtime, a test framework or another library without the suite's symbols. File and
/// line come from the portable PDB the SDK writes by default; a frame without them is skipped.
/// </summary>
internal static class ProtoSourceLocator
{
    public const string FilePathAttribute = "code.file.path";
    public const string LineNumberAttribute = "code.line.number";
    public const string FunctionAttribute = "code.function.name";

    private static readonly ConcurrentDictionary<Assembly, bool> Skipped = new();
    private static readonly ConcurrentDictionary<string, string> Recorded = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string?> RepositoryRoots = new(StringComparer.OrdinalIgnoreCase);

    // Libraries that call into a suite but are never the suite itself.
    private static readonly string[] SkippedPrefixes =
    [
        "System.", "Microsoft.", "netstandard", "mscorlib", "nunit", "NUnit", "xunit", "MSTest", "TUnit",
        "Castle.", "Grpc.", "Google.", "Npgsql", "Testcontainers", "Docker.", "RabbitMQ.", "OpenQA.", "Polly",
    ];

    public static (string File, int Line, string Function)? Find()
    {
        StackFrame[] frames;
        try
        {
            frames = new StackTrace(2, fNeedFileInfo: true).GetFrames();
        }
        catch (Exception)
        {
            return null;
        }

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (method is null || type is null) continue;
            // Past a continuation, the frames below belong to whatever completed the awaited task, not to the
            // code that started this operation: ProtoTest's lifecycle resuming after a suite's attribute, say.
            // A direct call only passes through a method builder's Start.
            if (IsContinuationBoundary(type, method)) return null;
            if (IsSkipped(type.Assembly)) continue;

            var file = frame.GetFileName();
            var line = frame.GetFileLineNumber();
            if (string.IsNullOrEmpty(file) || line <= 0) continue;

            return (Record(file), line, Describe(type, method));
        }

        return null;
    }

    /// <summary>The file a recorded path stands for, so the archive writer can embed it.</summary>
    public static string Resolve(string recordedPath) => Recorded.GetValueOrDefault(recordedPath, recordedPath);

    // A path inside a git repository is recorded relative to its root, with forward slashes: the same on every
    // machine and in CI, and it does not put the local directory layout into a trace that gets shared.
    private static string Record(string file)
    {
        var root = RepositoryRoots.GetOrAdd(Path.GetDirectoryName(file) ?? string.Empty, FindRepositoryRoot);
        var recorded = root is null ? file : Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
        Recorded.TryAdd(recorded, file);
        return recorded;
    }

    private static string? FindRepositoryRoot(string directory)
    {
        for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // .git is a directory in a clone and a file in a worktree or submodule.
            var marker = Path.Combine(current, ".git");
            if (Directory.Exists(marker) || File.Exists(marker)) return current;
        }

        return null;
    }

    private static bool IsContinuationBoundary(Type type, MethodBase method)
        => method.Name != "Start"
           && type.Namespace is "System.Threading.Tasks" or "System.Threading" or "System.Runtime.CompilerServices";

    private static bool IsSkipped(Assembly assembly) => Skipped.GetOrAdd(assembly, static candidate =>
    {
        var name = candidate.GetName().Name ?? string.Empty;
        if (SkippedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))) return true;
        // Every ProtoTest package carries this marker; a suite's own assemblies do not, whatever their name.
        return candidate.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute => attribute.Key == "ProtoTest.Framework" && attribute.Value == "true");
    });

    // An async method runs as the MoveNext of a generated state machine, <Name>d__N; name the method itself.
    private static string Describe(Type type, MethodBase method)
    {
        var declaring = type;
        var name = method.Name;
        if (name == "MoveNext" && type.Name.StartsWith('<') && type.DeclaringType is not null)
        {
            declaring = type.DeclaringType;
            var end = type.Name.IndexOf('>');
            if (end > 1) name = type.Name[1..end];
        }

        return $"{declaring.FullName?.Replace('+', '.')}.{name}";
    }
}
