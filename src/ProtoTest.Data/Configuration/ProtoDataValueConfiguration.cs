namespace ProtoTest.Data;

using ProtoTest.Data.Internal;

/// <summary>Configures providers that supply a value for every member of a CLR type.</summary>
public sealed class ProtoDataValueConfiguration
{
    private readonly ProtoDataRegistry _registry;
    private readonly Func<string> _source;

    internal ProtoDataValueConfiguration(ProtoDataRegistry registry, Func<string> source)
    {
        _registry = registry;
        _source = source;
    }

    /// <summary>Uses the provider for every member of <typeparamref name="T"/>.</summary>
    public ProtoDataValueConfiguration Use<T>(Func<ProtoDataValueContext, T> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _registry.AddTypeProvider(typeof(T), context => provider(context), _source());
        return this;
    }
}
