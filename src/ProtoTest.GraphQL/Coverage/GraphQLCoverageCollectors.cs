namespace ProtoTest.GraphQL;

using System.Text.Json;
using HotChocolate.Language;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class GraphQLCoverageCollector(string targetName) : ProtoCoverageCollector(targetName)
{
    public override string Category => "GraphQL operation";
    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation) && observation.Kind == "graphql.response";
}

public sealed class GraphQLSchemaCoverageCollector : ProtoCoverageCollector
{
    private readonly GraphQLSchemaIndex _schema;
    private readonly Dictionary<string, int> _fieldHits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _argumentHits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _inputFieldHits = new(StringComparer.Ordinal);

    public GraphQLSchemaCoverageCollector(
        string targetName,
        IConfiguration configuration,
        IEnumerable<ProtoApplicationTarget> applicationTargets) : base(targetName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var applicationName = ProtoApplicationTargets.ResolveApplication(targetName, applicationTargets);
        var application = ProtoApplication.Section(configuration, applicationName);
        var source = application["GraphQL:Schema"];
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException(ProtoApplication.MissingSettingMessage(applicationName, "GraphQL:Schema"));
        _schema = GraphQLSchemaIndex.Parse(ProtoDocumentSource.LoadText(source, application["BaseUrl"]));
    }

    public GraphQLSchemaCoverageCollector(string targetName, string schemaSource) : base(targetName)
        => _schema = GraphQLSchemaIndex.Parse(ProtoDocumentSource.LoadText(schemaSource));

    public override string Category => "GraphQL schema";

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation) && observation.Kind == "graphql.response" && observation.Data is GraphQLResponseData;

    public override void Collect(ProtoObservation observation)
    {
        if (observation.Data is not GraphQLResponseData response) return;
        var document = Utf8GraphQLParser.Parse(response.Document);
        var operation = document.Definitions.OfType<OperationDefinitionNode>()
            .FirstOrDefault(candidate => response.OperationName is null || candidate.Name?.Value == response.OperationName);
        if (operation is null) return;
        var fragments = document.Definitions.OfType<FragmentDefinitionNode>()
            .ToDictionary(fragment => fragment.Name.Value, StringComparer.Ordinal);
        var rootType = _schema.RootTypes.GetValueOrDefault(operation.Operation);
        if (rootType is null) return;
        var variableTypes = operation.VariableDefinitions.ToDictionary(
            definition => definition.Variable.Name.Value,
            definition => GraphQLSchemaIndex.NamedType(definition.Type),
            StringComparer.Ordinal);
        var variableValues = ReadVariables(response.VariablesJson);

        lock (_lock)
        {
            VisitSelections(rootType, operation.SelectionSet, fragments, new HashSet<string>(StringComparer.Ordinal),
                variableTypes, variableValues);
        }
    }

    public override IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (_lock)
        {
            return _schema.Types.Values
                .Where(type => !type.Name.StartsWith("__", StringComparison.Ordinal))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .Select(type => new ProtoReportItem(
                    TargetName,
                    "GraphQL type",
                    type.Name,
                    ProtoReportItemKind.Coverage,
                    type.Fields.Any(field => _fieldHits.GetValueOrDefault($"{type.Name}.{field.Name}") > 0)
                        ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                    Count: type.Fields.Sum(field => _fieldHits.GetValueOrDefault($"{type.Name}.{field.Name}")),
                    IsCovered: null,
                    Children: type.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field =>
                    {
                        var identifier = $"{type.Name}.{field.Name}";
                        var hits = _fieldHits.GetValueOrDefault(identifier);
                        return new ProtoReportItem(
                            TargetName,
                            "GraphQL field",
                            identifier,
                            ProtoReportItemKind.Coverage,
                            hits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                            Count: hits,
                            IsCovered: hits > 0,
                            Children: field.Arguments.Select(argument =>
                            {
                                var argumentIdentifier = $"{identifier}({argument.Name})";
                                var argumentHits = _argumentHits.GetValueOrDefault(argumentIdentifier);
                                return new ProtoReportItem(
                                    TargetName,
                                    "GraphQL argument",
                                    argumentIdentifier,
                                    ProtoReportItemKind.Coverage,
                                    argumentHits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                                    Count: argumentHits,
                                    IsCovered: argumentHits > 0,
                                    Metadata: new Dictionary<string, object> { ["type"] = argument.Type });
                            }).ToArray(),
                            Metadata: new Dictionary<string, object>
                            {
                                ["returnType"] = field.Type,
                                ["deprecated"] = field.Deprecated
                            });
                    }).ToArray()))
                .Concat(_schema.InputTypes.Values
                    .OrderBy(type => type.Name, StringComparer.Ordinal)
                    .Select(type => new ProtoReportItem(
                        TargetName,
                        "GraphQL input type",
                        type.Name,
                        ProtoReportItemKind.Coverage,
                        type.Fields.Any(field => _inputFieldHits.GetValueOrDefault($"{type.Name}.{field.Name}") > 0)
                            ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                        Count: type.Fields.Sum(field => _inputFieldHits.GetValueOrDefault($"{type.Name}.{field.Name}")),
                        IsCovered: null,
                        Children: type.Fields.Select(field =>
                        {
                            var identifier = $"{type.Name}.{field.Name}";
                            var hits = _inputFieldHits.GetValueOrDefault(identifier);
                            return new ProtoReportItem(
                                TargetName,
                                "GraphQL input field",
                                identifier,
                                ProtoReportItemKind.Coverage,
                                hits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                                Count: hits,
                                IsCovered: hits > 0,
                                Metadata: new Dictionary<string, object> { ["type"] = field.Type });
                        }).ToArray())))
                .ToArray();
        }
    }

    private void VisitSelections(string typeName, SelectionSetNode selections,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        HashSet<string> activeFragments,
        IReadOnlyDictionary<string, string> variableTypes,
        IReadOnlyDictionary<string, JsonElement> variableValues)
    {
        if (!_schema.Types.TryGetValue(typeName, out var type)) return;
        foreach (var selection in selections.Selections)
        {
            switch (selection)
            {
                case FieldNode field:
                    var definition = type.Fields.FirstOrDefault(candidate => candidate.Name == field.Name.Value);
                    if (definition is null) continue;
                    var identifier = $"{typeName}.{definition.Name}";
                    _fieldHits[identifier] = _fieldHits.GetValueOrDefault(identifier) + 1;
                    foreach (var argument in field.Arguments)
                    {
                        var argumentDefinition = definition.Arguments.FirstOrDefault(candidate => candidate.Name == argument.Name.Value);
                        if (argumentDefinition is null) continue;
                        var argumentIdentifier = $"{identifier}({argumentDefinition.Name})";
                        _argumentHits[argumentIdentifier] = _argumentHits.GetValueOrDefault(argumentIdentifier) + 1;
                        VisitInputValue(argumentDefinition.NamedType, argument.Value, variableTypes, variableValues);
                    }
                    if (field.SelectionSet is not null)
                        VisitSelections(definition.NamedType, field.SelectionSet, fragments, activeFragments, variableTypes, variableValues);
                    break;
                case InlineFragmentNode inlineFragment:
                    VisitSelections(inlineFragment.TypeCondition?.Name.Value ?? typeName, inlineFragment.SelectionSet, fragments, activeFragments, variableTypes, variableValues);
                    break;
                case FragmentSpreadNode spread when fragments.TryGetValue(spread.Name.Value, out var fragment)
                    && activeFragments.Add(spread.Name.Value):
                    VisitSelections(fragment.TypeCondition.Name.Value, fragment.SelectionSet, fragments, activeFragments, variableTypes, variableValues);
                    activeFragments.Remove(spread.Name.Value);
                    break;
            }
        }
    }

    private void VisitInputValue(
        string inputTypeName,
        IValueNode value,
        IReadOnlyDictionary<string, string> variableTypes,
        IReadOnlyDictionary<string, JsonElement> variableValues)
    {
        if (value is VariableNode variable && variableValues.TryGetValue(variable.Name.Value, out var suppliedVariable))
        {
            VisitInputJson(variableTypes.GetValueOrDefault(variable.Name.Value) ?? inputTypeName, suppliedVariable);
            return;
        }
        if (value is ListValueNode list)
        {
            foreach (var item in list.Items) VisitInputValue(inputTypeName, item, variableTypes, variableValues);
            return;
        }
        if (value is not ObjectValueNode input || !_schema.InputTypes.TryGetValue(inputTypeName, out var inputType)) return;
        foreach (var suppliedField in input.Fields)
        {
            var definition = inputType.Fields.FirstOrDefault(field => field.Name == suppliedField.Name.Value);
            if (definition is null) continue;
            var identifier = $"{inputTypeName}.{definition.Name}";
            _inputFieldHits[identifier] = _inputFieldHits.GetValueOrDefault(identifier) + 1;
            VisitInputValue(definition.NamedType, suppliedField.Value, variableTypes, variableValues);
        }
    }

    private void VisitInputJson(string inputTypeName, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) VisitInputJson(inputTypeName, item);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object || !_schema.InputTypes.TryGetValue(inputTypeName, out var inputType)) return;
        foreach (var suppliedProperty in value.EnumerateObject())
        {
            var definition = inputType.Fields.FirstOrDefault(field => field.Name == suppliedProperty.Name);
            if (definition is null) continue;
            var identifier = $"{inputTypeName}.{definition.Name}";
            _inputFieldHits[identifier] = _inputFieldHits.GetValueOrDefault(identifier) + 1;
            VisitInputJson(definition.NamedType, suppliedProperty.Value);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadVariables(string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson)) return new Dictionary<string, JsonElement>();
        try
        {
            using var document = JsonDocument.Parse(variablesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, JsonElement>();
            return document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }
}

internal sealed class GraphQLSchemaIndex
{
    private GraphQLSchemaIndex(
        Dictionary<string, GraphQLSchemaType> types,
        Dictionary<string, GraphQLSchemaInputType> inputTypes,
        Dictionary<OperationType, string> rootTypes)
    {
        Types = types;
        InputTypes = inputTypes;
        RootTypes = rootTypes;
    }

    public Dictionary<string, GraphQLSchemaType> Types { get; }
    public Dictionary<string, GraphQLSchemaInputType> InputTypes { get; }
    public Dictionary<OperationType, string> RootTypes { get; }

    public static GraphQLSchemaIndex Parse(string schemaText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaText);
        var document = Utf8GraphQLParser.Parse(schemaText);
        var types = new Dictionary<string, GraphQLSchemaType>(StringComparer.Ordinal);
        var inputTypes = new Dictionary<string, GraphQLSchemaInputType>(StringComparer.Ordinal);
        foreach (var definition in document.Definitions)
        {
            if (definition is ObjectTypeDefinitionNode objectType)
                types[objectType.Name.Value] = CreateType(objectType.Name.Value, objectType.Fields);
            else if (definition is InterfaceTypeDefinitionNode interfaceType)
                types[interfaceType.Name.Value] = CreateType(interfaceType.Name.Value, interfaceType.Fields);
            else if (definition is InputObjectTypeDefinitionNode inputType)
                inputTypes[inputType.Name.Value] = CreateInputType(inputType.Name.Value, inputType.Fields);
        }
        foreach (var definition in document.Definitions)
        {
            if (definition is ObjectTypeExtensionNode objectExtension)
                MergeType(types, objectExtension.Name.Value, objectExtension.Fields);
            else if (definition is InterfaceTypeExtensionNode interfaceExtension)
                MergeType(types, interfaceExtension.Name.Value, interfaceExtension.Fields);
        }

        var roots = new Dictionary<OperationType, string>
        {
            [OperationType.Query] = "Query",
            [OperationType.Mutation] = "Mutation",
            [OperationType.Subscription] = "Subscription"
        };
        var schema = document.Definitions.OfType<SchemaDefinitionNode>().FirstOrDefault();
        if (schema is not null)
            foreach (var operation in schema.OperationTypes) roots[operation.Operation] = operation.Type.Name.Value;
        foreach (var extension in document.Definitions.OfType<SchemaExtensionNode>())
            foreach (var operation in extension.OperationTypes) roots[operation.Operation] = operation.Type.Name.Value;
        return new GraphQLSchemaIndex(types, inputTypes, roots);
    }

    private static void MergeType(
        Dictionary<string, GraphQLSchemaType> types,
        string name,
        IReadOnlyList<FieldDefinitionNode> fields)
    {
        var extension = CreateType(name, fields);
        types[name] = types.TryGetValue(name, out var existing)
            ? existing with { Fields = existing.Fields.Concat(extension.Fields).ToArray() }
            : extension;
    }

    private static GraphQLSchemaType CreateType(string name, IReadOnlyList<FieldDefinitionNode> fields)
        => new(name, fields.Select(field => new GraphQLSchemaField(
            field.Name.Value,
            field.Type.ToString(),
            NamedType(field.Type),
            field.Directives.Any(directive => directive.Name.Value == "deprecated"),
            field.Arguments.Select(argument => new GraphQLSchemaInputField(
                argument.Name.Value,
                argument.Type.ToString(),
                NamedType(argument.Type))).ToArray())).ToArray());

    private static GraphQLSchemaInputType CreateInputType(string name, IReadOnlyList<InputValueDefinitionNode> fields)
        => new(name, fields.Select(field => new GraphQLSchemaInputField(
            field.Name.Value,
            field.Type.ToString(),
            NamedType(field.Type))).ToArray());

    internal static string NamedType(ITypeNode type) => type switch
    {
        NamedTypeNode named => named.Name.Value,
        ListTypeNode list => NamedType(list.Type),
        NonNullTypeNode nonNull => NamedType(nonNull.Type),
        _ => type.ToString()
    };
}

internal sealed record GraphQLSchemaType(string Name, IReadOnlyList<GraphQLSchemaField> Fields);
internal sealed record GraphQLSchemaField(
    string Name,
    string Type,
    string NamedType,
    bool Deprecated,
    IReadOnlyList<GraphQLSchemaInputField> Arguments);
internal sealed record GraphQLSchemaInputType(string Name, IReadOnlyList<GraphQLSchemaInputField> Fields);
internal sealed record GraphQLSchemaInputField(string Name, string Type, string NamedType);
