namespace ProtoTest.Core;

/// <summary>One failed Run gate, as reported when the run ends.</summary>
public sealed record ProtoRunGateFailure(string Name, string? Message, IReadOnlyList<string> Details);

/// <summary>Thrown when one or more Run gates failed, failing the run.</summary>
public sealed class ProtoRunGateException : Exception
{
    public ProtoRunGateException(IReadOnlyList<ProtoRunGateFailure> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
    }

    public IReadOnlyList<ProtoRunGateFailure> Failures { get; }

    private static string BuildMessage(IReadOnlyList<ProtoRunGateFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return failures.Count == 1
            ? $"Run gate '{failures[0].Name}' failed: {failures[0].Message}"
            : $"{failures.Count} Run gates failed: {string.Join(", ", failures.Select(failure => failure.Name))}";
    }
}
