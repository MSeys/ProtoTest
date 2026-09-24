namespace ProtoTest.Data;

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ProtoTest.Core;
using ProtoTest.Data.Internal;

/// <summary>Builds one instance while keeping scenario-relevant values explicit.</summary>
public sealed partial class ProtoDataObjectBuilder<T>
{
    private const string TraceSource = "ProtoTest.Data";
    private readonly ProtoDataRegistry _registry;
    private readonly ProtoDataService _data;
    private readonly ProtoExecutionContext _execution;
    private readonly long _objectSequence;
    private readonly Dictionary<string, ExplicitValue> _explicitValues = new(StringComparer.OrdinalIgnoreCase);
    private ResolvedPlan? _plan;

    internal ProtoDataObjectBuilder(
        ProtoDataRegistry registry,
        ProtoDataService data,
        ProtoExecutionContext execution,
        long objectSequence)
    {
        _registry = registry;
        _data = data;
        _execution = execution;
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
        var context = _execution;
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
        var context = _execution;
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
        var context = _execution;
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
        var context = _execution;
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
        var context = _execution;
        using var operation = context.Trace
            .Operation("data.create_many", $"Create {count} · {typeof(T).Name} → {typeof(TResult).Name}", TraceSource)
            .With("data.type", typeof(T).FullName)
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

}
