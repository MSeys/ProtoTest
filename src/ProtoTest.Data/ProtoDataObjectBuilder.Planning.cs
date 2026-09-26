namespace ProtoTest.Data;

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ProtoTest.Core;
using ProtoTest.Data.Internal;

public sealed partial class ProtoDataObjectBuilder<T>
{
    private ResolvedPlan ResolvePlan(ProtoExecutionContext executionContext, string traceParentId)
    {
        if (_plan is not null)
        {
            return _plan;
        }

        var targetType = typeof(T);
        var constructor = SelectConstructor(targetType);
        var values = new List<ResolvedValue>();
        var constructorMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (constructor is not null)
        {
            foreach (var parameter in constructor.GetParameters())
            {
                var memberName = FindProperty(targetType, parameter.Name)?.Name ?? parameter.Name!;
                constructorMembers.Add(memberName);
                values.Add(ResolveValue(
                    executionContext,
                    memberName,
                    parameter.ParameterType,
                    parameter.HasDefaultValue,
                    parameter.DefaultValue,
                    isConstructorParameter: true,
                    traceParentId: traceParentId));
            }
        }

        foreach (var property in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(property => property.MetadataToken))
        {
            if (constructorMembers.Contains(property.Name))
            {
                continue;
            }

            if (property.SetMethod?.IsPublic == true)
            {
                values.Add(ResolveValue(
                    executionContext,
                    property.Name,
                    property.PropertyType,
                    hasDefaultValue: false,
                    defaultValue: null,
                    isConstructorParameter: false,
                    traceParentId: traceParentId));
            }
            else if (_explicitValues.ContainsKey(property.Name)
                     || property.IsDefined(typeof(RequiredMemberAttribute)))
            {
                throw new ProtoDataException(
                    $"Property '{targetType.FullName}.{property.Name}' must be set but is not writable and is not bound to the selected constructor.");
            }
        }

        var knownMembers = values.Select(value => value.MemberName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unusedExplicit = _explicitValues.Keys.FirstOrDefault(member => !knownMembers.Contains(member));
        if (unusedExplicit is not null)
        {
            throw new ProtoDataException(
                $"Explicit property '{targetType.FullName}.{unusedExplicit}' cannot be assigned by the selected construction route.");
        }

        _plan = new ResolvedPlan(constructor, values);
        return _plan;
    }

    private ResolvedValue ResolveValue(
        ProtoExecutionContext executionContext,
        string memberName,
        Type valueType,
        bool hasDefaultValue,
        object? defaultValue,
        bool isConstructorParameter,
        string traceParentId)
    {
        if (_explicitValues.TryGetValue(memberName, out var explicitValue))
        {
            return new(memberName, valueType, explicitValue.Value, "Explicit", "With(...)", isConstructorParameter);
        }

        var valueContext = new ProtoDataValueContext(
            executionContext.Services,
            _data,
            executionContext.TestId,
            _objectSequence,
            typeof(T),
            valueType,
            memberName);

        if (_registry.TryGetMemberProvider(typeof(T), memberName, out var memberProvider))
        {
            return new(memberName, valueType, Invoke(memberProvider, valueContext, memberName),
                "MemberDefault", memberProvider.Source, isConstructorParameter);
        }

        if (_registry.TryGetTypeProvider(valueType, out var typeProvider))
        {
            return new(memberName, valueType, Invoke(typeProvider, valueContext, memberName),
                "TypeProvider", typeProvider.Source, isConstructorParameter);
        }

        foreach (var resolver in _registry.Resolvers)
        {
            try
            {
                if (resolver.TryResolve(valueContext, out var custom))
                {
                    return new(memberName, valueType, custom.Value,
                        "CustomResolver", custom.Source, isConstructorParameter);
                }
            }
            catch (Exception resolverException)
            {
                throw new ProtoDataException(
                    $"Data resolver '{resolver.GetType().FullName}' failed while resolving '{typeof(T).FullName}.{memberName}'.",
                    resolverException);
            }
        }

        // An optional constructor parameter keeps the default its declaration gives it: the author wrote
        // the default as the intended value, and generating over it would silently produce data the
        // constructor never asked for. Built-in generation serves the members that have no default.
        if (hasDefaultValue)
        {
            return new(memberName, valueType, defaultValue, "ConstructorDefault", "Optional parameter default", true);
        }

        if (TryBuiltIn(valueType, valueContext, out var generated, out var source))
        {
            return new(memberName, valueType, generated, "BuiltIn", source, isConstructorParameter);
        }

        var exception = new ProtoDataException(
            $"ProtoTest.Data could not resolve '{typeof(T).FullName}.{memberName}' ({valueType.FullName}). " +
            "Set it with With(...), register a member default, or register a provider for its type.");
        executionContext.Trace.WriteEvent(
            "data.value.resolve",
            $"Resolve · {typeof(T).Name}.{memberName}",
            TraceSource,
            outcome: ProtoTraceOutcome.Failed,
            attributes: new Dictionary<string, string?>
            {
                ["data.type"] = typeof(T).FullName,
                ["data.member"] = memberName,
                ["data.value_type"] = valueType.FullName,
                ["data.source_kind"] = "Unresolved"
            },
            exception: exception,
            parentId: traceParentId);
        throw exception;
    }

    private static object? Invoke(
        ProtoDataRegistry.Registration registration,
        ProtoDataValueContext context,
        string memberName)
    {
        try
        {
            return registration.Provider(context);
        }
        catch (Exception exception)
        {
            throw new ProtoDataException(
                $"Data provider '{registration.Source}' failed while resolving '{typeof(T).FullName}.{memberName}'.",
                exception);
        }
    }

    private static bool TryBuiltIn(
        Type type,
        ProtoDataValueContext context,
        out object? value,
        out string source)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            value = null;
            source = "Nullable value";
            return true;
        }

        if (!type.IsValueType && IsNullableReference(type, context.TargetType, context.MemberName))
        {
            value = null;
            source = "Nullable reference";
            return true;
        }

        if (type == typeof(string))
        {
            value = context.NextString();
            source = "Deterministic string";
            return true;
        }

        if (type == typeof(Guid))
        {
            value = context.NextGuid();
            source = "Deterministic GUID";
            return true;
        }

        if (type.IsArray)
        {
            value = Array.CreateInstance(type.GetElementType()!, 0);
            source = "Empty collection";
            return true;
        }

        if (TryEmptyGenericCollection(type, out value))
        {
            source = "Empty collection";
            return true;
        }

        value = null;
        source = string.Empty;
        return false;
    }

    private static bool TryEmptyGenericCollection(Type type, out object? value)
    {
        if (!type.IsGenericType)
        {
            value = null;
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        var elementType = type.GetGenericArguments()[0];
        if (definition == typeof(IEnumerable<>)
            || definition == typeof(IReadOnlyCollection<>)
            || definition == typeof(IReadOnlyList<>))
        {
            value = Array.CreateInstance(elementType, 0);
            return true;
        }

        if (definition == typeof(ICollection<>) || definition == typeof(IList<>) || definition == typeof(List<>))
        {
            value = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
            return true;
        }

        value = null;
        return false;
    }

    private static bool IsNullableReference(Type type, Type targetType, string memberName)
    {
        var property = FindProperty(targetType, memberName);
        if (property is not null)
        {
            return new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;
        }

        var constructor = SelectConstructor(targetType);
        var parameter = constructor?.GetParameters()
            .FirstOrDefault(item => string.Equals(item.Name, memberName, StringComparison.OrdinalIgnoreCase));
        return parameter is not null
            && new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable;
    }

    private static ConstructorInfo? SelectConstructor(Type type)
    {
        if (type.IsValueType)
        {
            return null;
        }

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 1)
        {
            return constructors[0];
        }

        var parameterless = constructors.SingleOrDefault(constructor => constructor.GetParameters().Length == 0);
        if (parameterless is not null)
        {
            return parameterless;
        }

        if (constructors.Length == 0)
        {
            throw new ProtoDataException($"Type '{type.FullName}' has no public constructor.");
        }

        throw new ProtoDataException(
            $"Type '{type.FullName}' has multiple public constructors and no unambiguous parameterless construction route.");
    }

    private static PropertyInfo? FindProperty(Type type, string? name)
        => name is null ? null : type.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

}
