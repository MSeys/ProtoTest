namespace ProtoTest.Data.Internal;

using System.Collections;

internal sealed class ProtoDataRegistry
{
    private readonly Dictionary<Type, Registration> _typeProviders = [];
    private readonly Dictionary<(Type Type, string Member), Registration> _memberProviders = [];
    private readonly Dictionary<Type, FactoryRegistration> _factories = [];
    private readonly List<IProtoDataValueResolver> _resolvers = [];
    private readonly HashSet<Type> _redactedValueTypes = [];
    private readonly HashSet<(Type Type, string Member)> _redactedMembers = [];

    public void AddTypeProvider(Type type, Func<ProtoDataValueContext, object?> provider, string source)
        => Add(_typeProviders, type, new(provider, source), $"type provider for '{type}'");

    public void AddMemberProvider(Type type, string member, Func<ProtoDataValueContext, object?> provider, string source)
        => Add(_memberProviders, (type, member), new(provider, source), $"default for '{type}.{member}'");

    public void AddFactory(Type type, Func<ProtoDataConstructionContext, object?> factory, string source)
    {
        if (_factories.TryGetValue(type, out var existing))
        {
            throw new ProtoDataException(
                $"Ambiguous construction factory for '{type}'. Both '{existing.Source}' and '{source}' registered a factory.");
        }

        _factories.Add(type, new(factory, source));
    }

    public void RedactValueType(Type type) => _redactedValueTypes.Add(type);

    public void RedactMember(Type type, string member) => _redactedMembers.Add((type, member));

    public void AddResolver(IProtoDataValueResolver resolver) => _resolvers.Add(resolver);

    public bool TryGetTypeProvider(Type type, out Registration registration)
        => _typeProviders.TryGetValue(type, out registration!);

    public bool TryGetMemberProvider(Type type, string member, out Registration registration)
        => _memberProviders.TryGetValue((type, member), out registration!);

    public bool TryGetFactory(Type type, out FactoryRegistration registration)
        => _factories.TryGetValue(type, out registration!);

    public bool IsRedacted(Type targetType, string member, Type valueType)
    {
        if (_redactedMembers.Contains((targetType, member)))
        {
            return true;
        }

        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (_redactedValueTypes.Contains(type))
        {
            return true;
        }

        // A collection member is redacted when its element type is redacted.
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var elementType = type.IsArray
                ? type.GetElementType()
                : type.GetInterfaces().Append(type)
                    .FirstOrDefault(candidate => candidate.IsGenericType
                        && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GetGenericArguments()[0];
            if (elementType is not null)
            {
                return _redactedValueTypes.Contains(Nullable.GetUnderlyingType(elementType) ?? elementType);
            }
        }

        return false;
    }

    public IReadOnlyList<IProtoDataValueResolver> Resolvers => _resolvers;

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
