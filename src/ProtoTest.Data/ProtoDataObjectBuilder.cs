namespace ProtoTest.Data;

using ProtoTest.Data.Internal;

using ProtoTest.Core;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

/// <summary>Builds one instance while keeping scenario-relevant values explicit.</summary>
public sealed class ProtoDataObjectBuilder<T>
{
    private const string TraceSource = "ProtoTest.Data";
    private readonly ProtoDataRegistry _registry;
    private readonly ProtoDataService _data;
    private readonly long _objectSequence;
    private readonly Dictionary<string, ExplicitValue> _explicitValues = new(StringComparer.OrdinalIgnoreCase);
    private ResolvedPlan? _plan;

    internal ProtoDataObjectBuilder(ProtoDataRegistry registry, ProtoDataService data, long objectSequence)
    {
        _registry = registry;
        _data = data;
        _objectSequence = objectSequence;
    }

    /// <summary>Sets a scenario-relevant property explicitly.</summary>
    public ProtoDataObjectBuilder<T> With<TMember>(
        Expression<Func<T, TMember>> member,
        TMember value)
    {
        var property = ProtoDataExpression.Property(member);
        _explicitValues[property.Name] = new ExplicitValue(property, value);
        _plan = null;
        return this;
    }

    /// <summary>
    /// Resolves and describes all values without constructing the object. For types registered with a
    /// factory, only the explicit <c>With(...)</c> values and the construction source are described.
    /// </summary>
    public ProtoDataExplanation Explain()
    {
        var context = Proto.Context;
        using var operation = context.Trace
            .Operation("data.explain", $"Explain · {typeof(T).Name}", TraceSource)
            .With(BaseAttributes())
            .Begin();

        try
        {
            if (_registry.TryGetFactory(typeof(T), out var factory))
            {
                operation.SetAttribute("data.construction_source", factory.Source);
                operation.Succeed();
                return new ProtoDataExplanation(
                    typeof(T),
                    _explicitValues.Values.Select(value => new ProtoDataValueExplanation(
                        value.Property.Name,
                        value.Property.PropertyType,
                        value.Value,
                        "Explicit",
                        "With(...)"))
                    .ToArray(),
                    factory.Source);
            }

            var plan = ResolvePlan(context, operation.Id);
            TraceValues(context, operation.Id, plan);
            operation.SetAttribute("data.member_count", plan.Values.Count.ToString());
            operation.SetAttribute("data.construction_source", "Reflection");
            operation.Succeed();
            return plan.Explanation;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Constructs the configured object and records value provenance in ProtoTrace.</summary>
    public T Build()
    {
        var context = Proto.Context;
        using var operation = context.Trace
            .Operation("data.build", $"Build · {typeof(T).Name}", TraceSource)
            .With(BaseAttributes())
            .Begin();

        try
        {
            if (_registry.TryGetFactory(typeof(T), out var factory))
            {
                var factoryInstance = ConstructWithFactory(context, operation.Id, factory, out var factoryValues);
                TraceValues(context, operation.Id, factoryValues);
                operation.SetAttribute("data.member_count", factoryValues.Count.ToString());
                operation.SetAttribute("data.construction_source", factory.Source);
                operation.Succeed();
                return factoryInstance;
            }

            var plan = ResolvePlan(context, operation.Id);
            TraceValues(context, operation.Id, plan);
            var instance = Construct(plan);
            operation.SetAttribute("data.member_count", plan.Values.Count.ToString());
            operation.SetAttribute("data.construction_source", "Reflection");
            operation.Succeed();
            return instance;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Builds the value and places it into the application using its registered provisioner.</summary>
    public async ValueTask<T> CreateAsync(CancellationToken cancellationToken = default)
        => await CreateAsync<T>(cancellationToken);

    /// <summary>Builds the input and provisions a different application result type.</summary>
    public async ValueTask<TResult> CreateAsync<TResult>(CancellationToken cancellationToken = default)
    {
        var context = Proto.Context;
        using var operation = context.Trace
            .Operation("data.create", $"Create · {typeof(T).Name}", TraceSource)
            .With(BaseAttributes())
            .Begin();

        try
        {
            var value = Build();
            var result = await _data.ProvisionAsync<T, TResult>(value, context, operation.Id, cancellationToken);
            operation.SetAttribute("data.identity", result.Identity);
            operation.Succeed();
            return result.Value;
        }
        catch (OperationCanceledException exception)
        {
            operation.Cancel(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Builds multiple independently resolved instances with deterministic unique sequences.</summary>
    public IReadOnlyList<T> BuildMany(
        int count,
        Action<ProtoDataObjectBuilder<T>, int>? configure = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var context = Proto.Context;
        using var operation = context.Trace
            .Operation("data.build_many", $"Build {count} · {typeof(T).Name}", TraceSource)
            .With("data.type", typeof(T).FullName)
            .With("data.count", count.ToString())
            .Begin();

        try
        {
            var results = new T[count];
            for (var index = 0; index < count; index++)
            {
                var builder = CreateItemBuilder();
                configure?.Invoke(builder, index);
                results[index] = builder.Build();
            }

            operation.Succeed();
            return results;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Builds and provisions multiple independently resolved instances.</summary>
    public async ValueTask<IReadOnlyList<TResult>> CreateManyAsync<TResult>(
        int count,
        Action<ProtoDataObjectBuilder<T>, int>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var context = Proto.Context;
        using var operation = context.Trace
            .Operation("data.create_many", $"Create {count} · {typeof(T).Name} → {typeof(TResult).Name}", TraceSource)
            .With("data.input_type", typeof(T).FullName)
            .With("data.result_type", typeof(TResult).FullName)
            .With("data.count", count.ToString())
            .Begin();

        try
        {
            var results = new TResult[count];
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var builder = CreateItemBuilder();
                configure?.Invoke(builder, index);
                results[index] = await builder.CreateAsync<TResult>(cancellationToken);
            }

            operation.Succeed();
            return results;
        }
        catch (OperationCanceledException exception)
        {
            operation.Cancel(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Builds and provisions multiple instances when input and result types are equal.</summary>
    public ValueTask<IReadOnlyList<T>> CreateManyAsync(
        int count,
        Action<ProtoDataObjectBuilder<T>, int>? configure = null,
        CancellationToken cancellationToken = default)
        => CreateManyAsync<T>(count, configure, cancellationToken);

    private ProtoDataObjectBuilder<T> CreateItemBuilder()
    {
        var builder = _data.For<T>();
        foreach (var explicitValue in _explicitValues.Values)
        {
            builder._explicitValues[explicitValue.Property.Name] = explicitValue;
        }

        return builder;
    }

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

        if (TryBuiltIn(valueType, valueContext, out var generated, out var source))
        {
            return new(memberName, valueType, generated, "BuiltIn", source, isConstructorParameter);
        }

        if (hasDefaultValue)
        {
            return new(memberName, valueType, defaultValue, "ConstructorDefault", "Optional parameter default", true);
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

    private static T Construct(ResolvedPlan plan)
    {
        try
        {
            object instance;
            if (typeof(T).IsValueType)
            {
                instance = Activator.CreateInstance(typeof(T))!;
            }
            else
            {
                var arguments = plan.Values
                    .Where(value => value.IsConstructorParameter)
                    .Select(value => value.Value)
                    .ToArray();
                instance = plan.Constructor!.Invoke(arguments);
            }

            foreach (var value in plan.Values.Where(value => !value.IsConstructorParameter))
            {
                FindProperty(typeof(T), value.MemberName)!.SetValue(instance, value.Value);
            }

            return (T)instance;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new ProtoDataException(
                $"Construction of '{typeof(T).FullName}' failed: {exception.InnerException.Message}",
                exception.InnerException);
        }
        catch (ProtoDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProtoDataException($"Construction of '{typeof(T).FullName}' failed.", exception);
        }
    }

    private T ConstructWithFactory(
        ProtoExecutionContext executionContext,
        string traceParentId,
        ProtoDataRegistry.FactoryRegistration factory,
        out IReadOnlyList<ResolvedValue> resolvedValues)
    {
        var values = new Dictionary<string, ResolvedValue>(StringComparer.OrdinalIgnoreCase);
        var constructionContext = new ProtoDataConstructionContext(
            executionContext.Services,
            (memberName, valueType) =>
            {
                if (values.TryGetValue(memberName, out var existing))
                {
                    if (existing.ValueType != valueType)
                    {
                        throw new ProtoDataException(
                            $"Factory '{factory.Source}' resolved '{memberName}' as both '{existing.ValueType}' and '{valueType}'.");
                    }

                    return existing.Value;
                }

                var resolved = ResolveValue(
                    executionContext,
                    memberName,
                    valueType,
                    hasDefaultValue: false,
                    defaultValue: null,
                    isConstructorParameter: true,
                    traceParentId: traceParentId);
                values.Add(memberName, resolved);
                return resolved.Value;
            });

        object? created;
        try
        {
            created = factory.Factory(constructionContext);
        }
        catch (ProtoDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProtoDataException(
                $"Construction factory '{factory.Source}' failed for '{typeof(T).FullName}'.",
                exception);
        }

        if (created is not T instance)
        {
            throw new ProtoDataException(
                $"Construction factory '{factory.Source}' returned null or an incompatible value for '{typeof(T).FullName}'.");
        }

        var unusedExplicit = _explicitValues.Keys.FirstOrDefault(member => !values.ContainsKey(member));
        if (unusedExplicit is not null)
        {
            throw new ProtoDataException(
                $"Construction factory '{factory.Source}' did not consume explicit value '{typeof(T).FullName}.{unusedExplicit}'.");
        }

        resolvedValues = values.Values.ToArray();
        return instance;
    }

    private IReadOnlyDictionary<string, string?> BaseAttributes()
        => new Dictionary<string, string?>
        {
            ["data.type"] = typeof(T).FullName,
            ["data.object_sequence"] = _objectSequence.ToString()
        };

    private void TraceValues(ProtoExecutionContext context, string parentId, ResolvedPlan plan)
        => TraceValues(context, parentId, plan.Values);

    private void TraceValues(ProtoExecutionContext context, string parentId, IReadOnlyList<ResolvedValue> values)
    {
        foreach (var value in values)
        {
            var redacted = _registry.IsRedacted(typeof(T), value.MemberName, value.ValueType);
            context.Trace.WriteEvent(
                "data.value.resolve",
                $"Resolve · {typeof(T).Name}.{value.MemberName}",
                TraceSource,
                outcome: ProtoTraceOutcome.Succeeded,
                attributes: new Dictionary<string, string?>
                {
                    ["data.type"] = typeof(T).FullName,
                    ["data.member"] = value.MemberName,
                    ["data.value_type"] = value.ValueType.FullName,
                    ["data.value"] = redacted ? "[REDACTED]" : ProtoTraceValueFormatter.Serialize(value.Value),
                    ["data.redacted"] = redacted.ToString().ToLowerInvariant(),
                    ["data.source_kind"] = value.SourceKind,
                    ["data.source"] = value.Source
                },
                parentId: parentId);
        }
    }

    private sealed record ExplicitValue(PropertyInfo Property, object? Value);

    private sealed record ResolvedValue(
        string MemberName,
        Type ValueType,
        object? Value,
        string SourceKind,
        string Source,
        bool IsConstructorParameter)
    {
        public ProtoDataValueExplanation Explanation
            => new(MemberName, ValueType, Value, SourceKind, Source);
    }

    private sealed record ResolvedPlan(ConstructorInfo? Constructor, IReadOnlyList<ResolvedValue> Values)
    {
        public ProtoDataExplanation Explanation
            => new(typeof(T), Values.Select(value => value.Explanation).ToArray(), "Reflection");
    }
}
