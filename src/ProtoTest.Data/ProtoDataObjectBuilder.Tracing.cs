namespace ProtoTest.Data;

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ProtoTest.Core;
using ProtoTest.Data.Internal;

public sealed partial class ProtoDataObjectBuilder<T>
{
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
            var redacted = _registry.IsRedacted(
                typeof(T), value.MemberName, value.ValueType, value.Value?.GetType());
            string? serialized;
            if (redacted)
            {
                serialized = "[REDACTED]";
            }
            else
            {
                // The declared and runtime types are not enough: a List<object> or a nested graph can
                // hold a redacted value the top-level check cannot see, so the graph is walked too.
                (serialized, var nestedRedacted) = _registry.RedactGraph(value.Value);
                redacted = nestedRedacted;
            }

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
                    ["data.value"] = serialized,
                    ["data.redacted"] = redacted.ToString().ToLowerInvariant(),
                    ["data.source_kind"] = value.SourceKind,
                    ["data.source"] = value.Source
                },
                parentId: parentId);
        }
    }

}
