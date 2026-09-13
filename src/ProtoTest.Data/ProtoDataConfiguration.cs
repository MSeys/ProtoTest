namespace ProtoTest.Data;

using System.Linq.Expressions;
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
    public ProtoDataConfiguration AddDefaults<TModule>() where TModule : IDataDefaultsModule, new()
    {
        AddModule(new TModule());
        return this;
    }

    /// <summary>Discovers and adds public, concrete defaults modules from an assembly.</summary>
    public ProtoDataConfiguration AddDefaultsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var moduleTypes = assembly.DefinedTypes
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IDataDefaultsModule).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        foreach (var moduleType in moduleTypes)
        {
            AddModule((IDataDefaultsModule)Activator.CreateInstance(moduleType.AsType())!);
        }

        return this;
    }

    private void AddModule(IDataDefaultsModule module)
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

/// <summary>Configures defaults for a particular object type.</summary>
public sealed class ProtoDataTypeConfiguration<T>
{
    private readonly ProtoDataRegistry _registry;
    private readonly Func<string> _source;

    internal ProtoDataTypeConfiguration(ProtoDataRegistry registry, Func<string> source)
    {
        _registry = registry;
        _source = source;
    }

    public ProtoDataTypeConfiguration<T> Default<TMember>(
        Expression<Func<T, TMember>> member,
        TMember value)
        => Default(member, _ => value);

    public ProtoDataTypeConfiguration<T> Default<TMember>(
        Expression<Func<T, TMember>> member,
        Func<ProtoDataValueContext, TMember> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var property = ProtoDataExpression.Property(member);
        _registry.AddMemberProvider(typeof(T), property.Name, context => provider(context), _source());
        return this;
    }

    /// <summary>Uses a domain-aware factory instead of reflection construction.</summary>
    public ProtoDataTypeConfiguration<T> ConstructUsing(Func<ProtoDataConstructionContext, T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _registry.AddFactory(typeof(T), context => factory(context), _source());
        return this;
    }

    /// <summary>Hides this member's value in ProtoTrace while retaining its provenance.</summary>
    public ProtoDataTypeConfiguration<T> Redact<TMember>(Expression<Func<T, TMember>> member)
    {
        var property = ProtoDataExpression.Property(member);
        _registry.RedactMember(typeof(T), property.Name);
        return this;
    }
}

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

/// <summary>Implemented by feature-local collections of data defaults.</summary>
public interface IDataDefaultsModule
{
    void Configure(ProtoDataConfiguration data);
}
