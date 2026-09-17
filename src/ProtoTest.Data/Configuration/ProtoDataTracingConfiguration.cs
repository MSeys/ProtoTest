namespace ProtoTest.Data;

using ProtoTest.Data.Internal;

/// <summary>Configures trace value redaction without disabling resolution diagnostics.</summary>
public sealed class ProtoDataTracingConfiguration
{
    private readonly ProtoDataRegistry _registry;

    internal ProtoDataTracingConfiguration(ProtoDataRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Redacts every resolved value having the specified CLR type.</summary>
    public ProtoDataTracingConfiguration RedactValues<T>()
    {
        _registry.RedactValueType(typeof(T));
        return this;
    }
}
