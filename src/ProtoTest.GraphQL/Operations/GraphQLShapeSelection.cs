namespace ProtoTest.GraphQL;

using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoTest.Json;

internal static class GraphQLShapeSelection
{
    private static readonly JsonSerializerOptions Naming = new(JsonSerializerDefaults.Web);

    public static void AddArguments(
        GraphQLOperationBuilder operation,
        GraphQLFieldBuilder field,
        object? arguments,
        IDictionary<string, object?> variables)
    {
        if (arguments is null) return;
        foreach (var (name, value, _) in Properties(arguments, arguments.GetType()))
        {
            if (value is GraphQLVariableValue variable)
            {
                operation.Variable(name, variable.Type);
                field.Argument(name, Gql.Var(name));
                variables[name] = variable.Value;
            }
            else if (value is GraphQLUpload upload)
            {
                operation.Variable(name, GqlType.Upload.NonNull());
                field.Argument(name, Gql.Var(name));
                variables[name] = upload;
            }
            else
            {
                field.Argument(name, value);
            }
        }
    }

    public static void Apply(GraphQLFieldBuilder field, object? shape, Type declaredType)
    {
        var nodes = SelectionNodes(shape, declaredType);
        if (nodes.Count == 0) return;
        field.Select(selection => AddNodes(selection, nodes));
    }

    private static void AddNodes(GraphQLSelectionBuilder selection, IReadOnlyList<SelectionNode> nodes)
    {
        foreach (var node in nodes)
            selection.Field(node.Name, field =>
            {
                if (node.Children.Count > 0)
                    field.Select(child => AddNodes(child, node.Children));
            });
    }

    private static IReadOnlyList<SelectionNode> SelectionNodes(object? value, Type declaredType)
    {
        declaredType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (IsLeaf(value, declaredType)) return [];

        // A dictionary is an object shape, not a sequence of its key/value pairs: IDictionary must be
        // checked before IEnumerable because every IDictionary implementation enumerates as pairs.
        if (value is IDictionary)
        {
            var entries = Properties(value, declaredType).ToArray();
            if (entries.Length == 0)
                throw new ArgumentException(
                    $"GraphQL selection type '{declaredType.Name}' has no selectable public properties.",
                    nameof(value));
            return entries.Select(entry => new SelectionNode(
                entry.Name,
                SelectionNodes(entry.Value, entry.Type))).ToArray();
        }

        // A dictionary declared without an instance has no entries to derive a selection from.
        if (value is null && typeof(IDictionary).IsAssignableFrom(declaredType)) return [];

        if (TryEnumerableElement(value, declaredType, out var item, out var itemType))
            return SelectionNodes(item, itemType);

        var properties = Properties(value, declaredType).ToArray();
        if (properties.Length == 0)
            throw new ArgumentException(
                $"GraphQL selection type '{declaredType.Name}' has no selectable public properties.",
                nameof(value));

        return properties.Select(property => new SelectionNode(
            property.Name,
            SelectionNodes(property.Value, property.Type))).ToArray();
    }

    private static IEnumerable<(string Name, object? Value, Type Type)> Properties(object? value, Type type)
    {
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                    throw new ArgumentException("GraphQL shape and argument dictionaries must use string keys.");
                yield return (key, entry.Value, entry.Value?.GetType() ?? typeof(object));
            }
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.GetIndexParameters().Length == 0
                         && property.GetCustomAttribute<JsonIgnoreAttribute>() is null))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? Naming.PropertyNamingPolicy!.ConvertName(property.Name);
            yield return (name, value is null ? null : property.GetValue(value), property.PropertyType);
        }
    }

    private static bool TryEnumerableElement(object? value, Type type, out object? item, out Type itemType)
    {
        item = null;
        itemType = typeof(object);
        if (type == typeof(string) || typeof(IDictionary).IsAssignableFrom(type) || !typeof(IEnumerable).IsAssignableFrom(type)) return false;

        itemType = type.IsArray
            ? type.GetElementType()!
            : type.GetInterfaces().Append(type)
                .FirstOrDefault(candidate => candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0] ?? typeof(object);
        if (value is IEnumerable sequence)
            item = sequence.Cast<object?>().FirstOrDefault(candidate => candidate is not null);
        return true;
    }

    private static bool IsLeaf(object? value, Type type)
        => value is IJsonValueMatcher or GraphQLFieldSelection
            || typeof(IJsonValueMatcher).IsAssignableFrom(type)
            || typeof(GraphQLFieldSelection).IsAssignableFrom(type)
            || type == typeof(object) && value is null
            || type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(Guid)
            || type == typeof(Uri);

    private sealed record SelectionNode(string Name, IReadOnlyList<SelectionNode> Children);
}
