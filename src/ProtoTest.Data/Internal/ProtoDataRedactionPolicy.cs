namespace ProtoTest.Data.Internal;

using System.Collections;
using System.Reflection;
using ProtoTest.Core;

/// <summary>
/// The data module's redaction rules: which types and members are sensitive, whether a resolved value
/// is redacted, and the graph walker that replaces them with <c>"[REDACTED]"</c> wherever they appear,
/// including through collections and nested objects. It owns its own lock so the store's registration
/// lock and the redaction policy stay independent.
/// </summary>
internal sealed class ProtoDataRedactionPolicy
{
    private readonly ProtoLock _gate = new();
    private readonly HashSet<Type> _redactedValueTypes = [];
    private readonly HashSet<(Type Type, string Member)> _redactedMembers = [];

    public void RedactValueType(Type type)
    {
        lock (_gate)
        {
            _redactedValueTypes.Add(type);
        }
    }

    public void RedactMember(Type type, string member)
    {
        lock (_gate)
        {
            _redactedMembers.Add((type, member));
        }
    }

    /// <summary>
    /// A value is redacted when its member is marked redacted or when the declared or runtime type -
    /// or a base type or interface it has - is a redacted value type. The runtime type matters because
    /// a member declared as <see langword="object"/> or an interface hides the type that is traced.
    /// </summary>
    public bool IsRedacted(Type targetType, string member, Type valueType, Type? runtimeType = null)
    {
        lock (_gate)
        {
            if (IsMemberRedacted(targetType, member))
            {
                return true;
            }

            return MatchesRedactedType(valueType)
                || (runtimeType is not null && MatchesRedactedType(runtimeType));
        }
    }

    /// <summary>
    /// Redacts a resolved value for tracing node by node: a redacted type or a redacted member anywhere
    /// in the graph becomes <c>"[REDACTED]"</c>, including values reached through collections and nested
    /// objects. Returns the serialized value and whether anything was redacted.
    /// </summary>
    public (string? Json, bool Redacted) RedactGraph(object? value)
    {
        if (value is null)
        {
            return (null, false);
        }

        lock (_gate)
        {
            var redacted = false;
            var graph = RedactNode(value, new HashSet<object>(ReferenceEqualityComparer.Instance), ref redacted);
            return (ProtoTraceValueFormatter.Serialize(graph), redacted);
        }
    }

    private object? RedactNode(object value, HashSet<object> ancestors, ref bool redacted)
    {
        var type = value.GetType();
        if (MatchesRedactedType(type))
        {
            redacted = true;
            return "[REDACTED]";
        }

        if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
        {
            return value;
        }

        if (!type.IsValueType && !ancestors.Add(value))
        {
            // A reference back to an object on the current path would recurse forever; the marker
            // keeps the walk finite and the value cannot leak through a cut branch.
            return "[circular]";
        }

        try
        {
            if (value is IDictionary dictionary)
            {
                var changed = false;
                var copy = new Dictionary<object, object?>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    var item = entry.Value is null
                        ? null
                        : RedactNode(entry.Value, ancestors, ref redacted);
                    changed |= !ReferenceEquals(item, entry.Value);
                    copy[entry.Key] = item;
                }

                return changed ? copy : value;
            }

            if (value is IEnumerable enumerable)
            {
                var changed = false;
                var copy = new List<object?>();
                foreach (var item in enumerable)
                {
                    var node = item is null ? null : RedactNode(item, ancestors, ref redacted);
                    changed |= !ReferenceEquals(node, item);
                    copy.Add(node);
                }

                return changed ? copy : value;
            }

            var changedProperties = false;
            var values = new Dictionary<string, object?>();
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
            {
                object? propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }

                if (IsMemberRedacted(type, property.Name))
                {
                    redacted = true;
                    changedProperties = true;
                    values[property.Name] = "[REDACTED]";
                    continue;
                }

                if (propertyValue is null)
                {
                    values[property.Name] = null;
                    continue;
                }

                var node = RedactNode(propertyValue, ancestors, ref redacted);
                changedProperties |= !ReferenceEquals(node, propertyValue);
                values[property.Name] = node;
            }

            return changedProperties ? values : value;
        }
        finally
        {
            if (!type.IsValueType)
            {
                ancestors.Remove(value);
            }
        }
    }

    /// <summary>Matches a redacted member against the type and its base types, so a base registration redacts derived values.</summary>
    private bool IsMemberRedacted(Type type, string member)
    {
        foreach (var (registeredType, registeredMember) in _redactedMembers)
        {
            if (string.Equals(registeredMember, member, StringComparison.Ordinal)
                && registeredType.IsAssignableFrom(type))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesRedactedType(Type valueType)
    {
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (IsRedactedType(type))
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
                return IsRedactedType(Nullable.GetUnderlyingType(elementType) ?? elementType);
            }
        }

        return false;
    }

    private bool IsRedactedType(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (_redactedValueTypes.Contains(current))
            {
                return true;
            }
        }

        return type.GetInterfaces().Any(_redactedValueTypes.Contains);
    }
}
