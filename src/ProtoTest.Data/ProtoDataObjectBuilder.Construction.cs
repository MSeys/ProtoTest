namespace ProtoTest.Data;

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ProtoTest.Core;
using ProtoTest.Data.Internal;

public sealed partial class ProtoDataObjectBuilder<T>
{
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
