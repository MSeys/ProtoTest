namespace ProtoTest.GraphQL;

using System.Collections;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed partial class GraphQLRequestBuilder
{
    public GraphQLRequestBuilder Query(string? name, Action<GraphQLOperationBuilder> configure)
        => Operation(GraphQLOperationKind.Query, name, configure);

    public GraphQLRequestBuilder Mutation(string? name, Action<GraphQLOperationBuilder> configure)
        => Operation(GraphQLOperationKind.Mutation, name, configure);

    public GraphQLRequestBuilder Subscription(string? name, Action<GraphQLOperationBuilder> configure)
        => Operation(GraphQLOperationKind.Subscription, name, configure);

    /// <summary>Starts a shape-driven query for one root field.</summary>
    public GraphQLRequestBuilder Query(string rootField, object? arguments = null, string? operationName = null)
        => SimpleOperation(GraphQLOperationKind.Query, rootField, arguments, operationName);

    /// <summary>Starts a shape-driven mutation for one root field.</summary>
    public GraphQLRequestBuilder Mutation(string rootField, object? arguments = null, string? operationName = null)
        => SimpleOperation(GraphQLOperationKind.Mutation, rootField, arguments, operationName);

    /// <summary>Starts a shape-driven subscription for one root field.</summary>
    public GraphQLRequestBuilder Subscription(string rootField, object? arguments = null, string? operationName = null)
        => SimpleOperation(GraphQLOperationKind.Subscription, rootField, arguments, operationName);

    /// <summary>Derives the GraphQL selection set from an anonymous object or test-owned contract.</summary>
    public GraphQLRequestBuilder Select<TShape>(TShape selectionShape)
    {
        ArgumentNullException.ThrowIfNull(selectionShape);
        return BuildSimpleSelection(selectionShape, selectionShape.GetType());
    }

    /// <summary>Derives the GraphQL selection set from a test-owned contract type.</summary>
    public GraphQLRequestBuilder Select<TShape>()
        => BuildSimpleSelection(null, typeof(TShape));

    /// <summary>Selects, executes, and matches one root field using the same response shape.</summary>
    public async Task<GraphQLResponse> ExpectAsync<TShape>(
        TShape expectedShape,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        _ = _shapePlan
            ?? throw new InvalidOperationException("ExpectAsync is available after a shape-driven Query or Mutation.");
        Select(expectedShape);
        var response = await ExecuteAsync(cancellationToken);
        try
        {
            response.ShouldMatchShape(expectedShape);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public GraphQLRequestBuilder Request(string document, string? operationName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        var parsed = HotChocolate.Language.Utf8GraphQLParser.Parse(document);
        var operation = parsed.Definitions.OfType<HotChocolate.Language.OperationDefinitionNode>()
            .FirstOrDefault(definition => operationName is null || definition.Name?.Value == operationName)
            ?? throw new ArgumentException("The GraphQL document does not contain the requested operation.", nameof(document));
        ClearSimpleOperation();
        _operation = new GraphQLBuiltOperation(
            document,
            GraphQLOperationKindExtensions.Parse(operation.Operation.ToString()),
            operation.Name?.Value);
        TraceConfiguration(
            "graphql.operation.configure",
            $"Configure · {_operation.Kind.WireName()} {_operation.Name ?? "<anonymous>"}",
            new Dictionary<string, string?>
            {
                ["graphql.operation.type"] = _operation.Kind.WireName(),
                ["graphql.operation.name"] = _operation.Name,
                ["graphql.document.source"] = "raw"
            });
        return this;
    }


    private GraphQLRequestBuilder Operation(
        GraphQLOperationKind kind,
        string? name,
        Action<GraphQLOperationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ClearSimpleOperation();
        var builder = new GraphQLOperationBuilder(kind, name);
        configure(builder);
        _operation = builder.Build();
        TraceConfiguration(
            "graphql.operation.configure",
            $"Configure · {kind.WireName()} {name ?? "<anonymous>"}",
            new Dictionary<string, string?>
            {
                ["graphql.operation.type"] = kind.WireName(),
                ["graphql.operation.name"] = name
            });
        return this;
    }

    private GraphQLRequestBuilder SimpleOperation(
        GraphQLOperationKind kind,
        string rootField,
        object? arguments,
        string? operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootField);
        _operation = null;
        _shapePlan = new ShapePlan(
            kind,
            rootField,
            arguments,
            operationName ?? char.ToUpperInvariant(rootField[0]) + rootField[1..]);
        return this;
    }

    private GraphQLRequestBuilder BuildSimpleSelection(object? shape, Type shapeType)
    {
        var plan = _shapePlan
            ?? throw new InvalidOperationException("Select is available after a shape-driven Query or Mutation.");
        var operation = new GraphQLOperationBuilder(plan.Kind, plan.OperationName);
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        operation.Field(plan.RootField, field =>
        {
            GraphQLShapeSelection.AddArguments(operation, field, plan.Arguments, variables);
            GraphQLShapeSelection.Apply(field, shape, shapeType);
        });
        _operation = operation.Build();
        // A shape that contributes no variables must not discard the ones the test set explicitly;
        // the shape's variables win on a name collision because the shape supplied them last.
        _variables = variables.Count == 0
            ? _variables
            : MergeVariables(_variables, variables);
        TraceConfiguration("graphql.operation.configure", $"Configure · {plan.Kind.WireName()} {plan.OperationName}",
            new Dictionary<string, string?>
            {
                ["graphql.operation.type"] = plan.Kind.WireName(),
                ["graphql.operation.name"] = plan.OperationName,
                ["graphql.root.field"] = plan.RootField,
                ["graphql.document.source"] = "shape"
            });
        return this;
    }

    private void ClearSimpleOperation() => _shapePlan = null;

    /// <summary>
    /// A shape-driven operation is configured before its selection exists: the root field and its
    /// arguments wait for the shape. One immutable record carries that pending state instead of four
    /// fields that had to be kept consistent with each other.
    /// </summary>
    private sealed record ShapePlan(
        GraphQLOperationKind Kind,
        string RootField,
        object? Arguments,
        string OperationName);

    /// <summary>
    /// Merges the shape's variables over the explicitly configured ones so a shape that contributes
    /// none does not discard <c>Variables(...)</c>. The shape wins on a name collision.
    /// </summary>
    private static Dictionary<string, object?> MergeVariables(
        object? explicitVariables,
        IReadOnlyDictionary<string, object?> shapeVariables)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (explicitVariables is not null)
        {
            foreach (var (name, value) in ReadVariables(explicitVariables))
                merged[name] = value;
        }

        foreach (var (name, value) in shapeVariables) merged[name] = value;
        return merged;
    }

    private static IEnumerable<(string Name, object? Value)> ReadVariables(object variables)
    {
        if (variables is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                if (entry.Key is string key)
                    yield return (key, entry.Value);
            yield break;
        }

        // JSON-typed variables are enumerated as they will be serialized, so a shape can merge over
        // them without reinterpreting their CLR shape.
        if (variables is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                yield return (property.Name, property.Value.Clone());
            yield break;
        }

        if (variables is JsonDocument document && document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
                yield return (property.Name, property.Value.Clone());
            yield break;
        }

        if (variables is System.Text.Json.Nodes.JsonObject jsonObject)
        {
            foreach (var pair in jsonObject)
                yield return (pair.Key, pair.Value);
            yield break;
        }

        // Property names are normalized exactly as GraphQLRequestContent serializes them, so the merged
        // dictionary keeps the wire names of the explicit variables.
        foreach (var property in variables.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            yield return (name, property.GetValue(variables));
        }
    }


}
