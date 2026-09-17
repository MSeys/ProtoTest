namespace ProtoTest.Data;

using ProtoTest.Data.Internal;

using System.Linq.Expressions;

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
