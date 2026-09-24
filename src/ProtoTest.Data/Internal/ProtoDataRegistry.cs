namespace ProtoTest.Data.Internal;

using System.Collections;
using System.Reflection;
using ProtoTest.Core;

internal sealed class ProtoDataRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<Type, Registration> _typeProviders = [];
    private readonly Dictionary<(Type Type, string Member), Registration> _memberProviders = [];
    private readonly Dictionary<Type, FactoryRegistration> _factories = [];
    private readonly List<IProtoDataValueResolver> _resolvers = [];

    /// <summary>The redaction rules registered for this module.</summary>
    public ProtoDataRedactionPolicy Redaction { get; } = new();

    public void AddTypeProvider(Type type, Func<ProtoDataValueContext, object?> provider, string source)
    {
        lock (_gate)
        {
            Add(_typeProviders, type, new(provider, source), $"type provider for '{type}'");
        }
    }

    public void AddMemberProvider(Type type, string member, Func<ProtoDataValueContext, object?> provider, string source)
    {
        lock (_gate)
        {
            Add(_memberProviders, (type, member), new(provider, source), $"default for '{type}.{member}'");
        }
    }

    public void AddFactory(Type type, Func<ProtoDataConstructionContext, object?> factory, string source)
    {
        lock (_gate)
        {
            if (_factories.TryGetValue(type, out var existing))
            {
                throw new ProtoDataException(
                    $"Ambiguous construction factory for '{type}'. Both '{existing.Source}' and '{source}' registered a factory.");
            }

            _factories.Add(type, new(factory, source));
        }
    }

    public void RedactValueType(Type type) => Redaction.RedactValueType(type);

    public void RedactMember(Type type, string member) => Redaction.RedactMember(type, member);

    public void AddResolver(IProtoDataValueResolver resolver)
    {
        lock (_gate)
        {
            _resolvers.Add(resolver);
        }
    }

    public bool TryGetTypeProvider(Type type, out Registration registration)
    {
        lock (_gate)
        {
            return _typeProviders.TryGetValue(type, out registration!);
        }
    }

    public bool TryGetMemberProvider(Type type, string member, out Registration registration)
    {
        registration = null!;
        lock (_gate)
        {
            // A default registered for a base type applies to every derived type: the member the
            // caller names still resolves to the same declaration.
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (_memberProviders.TryGetValue((current, member), out registration!))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool TryGetFactory(Type type, out FactoryRegistration registration)
    {
        lock (_gate)
        {
            return _factories.TryGetValue(type, out registration!);
        }
    }

    public bool IsRedacted(Type targetType, string member, Type valueType, Type? runtimeType = null)
        => Redaction.IsRedacted(targetType, member, valueType, runtimeType);

    /// <summary>
    /// Redacts a resolved value for tracing node by node. Returns the serialized value and whether
    /// anything was redacted.
    /// </summary>
    public (string? Json, bool Redacted) RedactGraph(object? value) => Redaction.RedactGraph(value);
    public IReadOnlyList<IProtoDataValueResolver> Resolvers
    {
        get
        {
            lock (_gate)
            {
                return [.. _resolvers];
            }
        }
    }

    private static void Add<TKey>(Dictionary<TKey, Registration> registrations, TKey key, Registration value, string description)
        where TKey : notnull
    {
        if (registrations.TryGetValue(key, out var existing))
        {
            throw new ProtoDataException(
                $"Ambiguous {description}. Both '{existing.Source}' and '{value.Source}' registered a value.");
        }

        registrations.Add(key, value);
    }

    internal sealed record Registration(Func<ProtoDataValueContext, object?> Provider, string Source);
    internal sealed record FactoryRegistration(Func<ProtoDataConstructionContext, object?> Factory, string Source);
}
