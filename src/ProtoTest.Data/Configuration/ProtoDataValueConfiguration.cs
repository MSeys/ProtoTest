namespace ProtoTest.Data;

using ProtoTest.Data.Internal;

/// <summary>Groups registrations for type-wide value providers.</summary>
public sealed class ProtoDataValueConfiguration
{
    private readonly ProtoDataRegistry _registry;
    private readonly Func<string> _source;

    internal ProtoDataValueConfiguration(ProtoDataRegistry registry, Func<string> source)
    {
        _registry = registry;
        _source = source;
    }

    public ProtoDataValueProviderConfiguration<T> For<T>() => new(_registry, _source);
}

/// <summary>Configures the provider for a CLR type.</summary>
public sealed class ProtoDataValueProviderConfiguration<T>
{
    private readonly ProtoDataRegistry _registry;
    private readonly Func<string> _source;

    internal ProtoDataValueProviderConfiguration(ProtoDataRegistry registry, Func<string> source)
    {
        _registry = registry;
        _source = source;
    }

    public ProtoDataValueProviderConfiguration<T> Use(Func<ProtoDataValueContext, T> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _registry.AddTypeProvider(typeof(T), context => provider(context), _source());
        return this;
    }
}
