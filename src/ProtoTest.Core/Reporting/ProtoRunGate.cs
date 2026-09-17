namespace ProtoTest.Core;

/// <summary>
/// Evaluates the evidence a run collected and can fail the run. Gates run once the suite has finished,
/// before reports are written, so a failed gate still produces its report.
/// </summary>
public interface IProtoRunGate
{
    string Name { get; }

    ProtoRunGateResult Evaluate(ProtoRunGateContext context);
}

/// <summary>A Run gate defined by a delegate.</summary>
public sealed class ProtoRunGate : IProtoRunGate
{
    private readonly Func<ProtoRunGateContext, ProtoRunGateResult> _evaluate;

    public ProtoRunGate(string name, Func<ProtoRunGateContext, ProtoRunGateResult> evaluate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(evaluate);
        Name = name;
        _evaluate = evaluate;
    }

    public string Name { get; }

    public ProtoRunGateResult Evaluate(ProtoRunGateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _evaluate(context);
    }
}
