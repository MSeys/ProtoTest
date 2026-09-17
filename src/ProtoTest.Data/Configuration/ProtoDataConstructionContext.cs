namespace ProtoTest.Data;

/// <summary>Resolves configured inputs for a domain-aware construction factory.</summary>
public sealed class ProtoDataConstructionContext
{
    private readonly Func<string, Type, object?> _resolve;

    internal ProtoDataConstructionContext(IServiceProvider services, Func<string, Type, object?> resolve)
    {
        Services = services;
        _resolve = resolve;
    }

    public IServiceProvider Services { get; }

    /// <summary>Resolves a named factory input through the normal ProtoTest.Data precedence rules.</summary>
    public TValue Value<TValue>(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        var value = _resolve(memberName, typeof(TValue));
        return value is null
            ? default!
            : (TValue)value;
    }
}
