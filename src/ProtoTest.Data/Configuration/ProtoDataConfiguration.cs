namespace ProtoTest.Data;

using ProtoTest.Data.Internal;

using System.Reflection;

/// <summary>Configures value providers and member defaults used by ProtoTest.Data.</summary>
public sealed class ProtoDataConfiguration
{
    private readonly ProtoDataRegistry _registry;
    private string _source = "Host configuration";

    internal ProtoDataConfiguration(ProtoDataRegistry registry)
    {
        _registry = registry;
        Values = new ProtoDataValueConfiguration(registry, () => _source);
        Tracing = new ProtoDataTracingConfiguration(registry);
    }

    /// <summary>Configures defaults that apply to a CLR type wherever it occurs.</summary>
    public ProtoDataValueConfiguration Values { get; }

    /// <summary>Configures how resolved values appear in ProtoTrace.</summary>
    public ProtoDataTracingConfiguration Tracing { get; }

    /// <summary>Configures defaults for individual members of <typeparamref name="T"/>.</summary>
    public ProtoDataTypeConfiguration<T> For<T>() => new(_registry, () => _source);

    /// <summary>Adds a convention-based resolver after exact member and type providers.</summary>
    public ProtoDataConfiguration AddValueResolver(IProtoDataValueResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _registry.AddResolver(resolver);
        return this;
    }

    /// <summary>Adds a defaults module with a public parameterless constructor.</summary>
    public ProtoDataConfiguration AddDefaults<TModule>() where TModule : IProtoDataDefaultsModule, new()
    {
        AddModule(new TModule());
        return this;
    }

    /// <summary>Discovers and adds public, concrete defaults modules from an assembly.</summary>
    public ProtoDataConfiguration AddDefaultsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var moduleTypes = assembly.DefinedTypes
            .Where(type => type is { IsClass: true, IsAbstract: false, IsVisible: true }
                && !type.ContainsGenericParameters
                && typeof(IProtoDataDefaultsModule).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        foreach (var moduleType in moduleTypes)
        {
            AddModule((IProtoDataDefaultsModule)Activator.CreateInstance(moduleType.AsType())!);
        }

        return this;
    }

    private void AddModule(IProtoDataDefaultsModule module)
    {
        var previous = _source;
        _source = module.GetType().FullName ?? module.GetType().Name;
        try
        {
            module.Configure(this);
        }
        finally
        {
            _source = previous;
        }
    }
}
