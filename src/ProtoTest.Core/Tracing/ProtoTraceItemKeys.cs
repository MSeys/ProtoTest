namespace ProtoTest.Core;

using System.Text;

/// <summary>
/// Builds the identity segments ProtoTrace uses for tracked values. A tracked value is the item kind
/// <c>value</c> with the id <c>{type}:{identity}</c>, where <c>{type}</c> is the CLR type name in
/// snake_case: an <c>InvoiceLine</c> becomes <c>invoice_line:42</c>. An application that emits its own
/// identity attribute (<c>invoice_line.number</c>) uses the same segment, so both sides describe one value.
/// </summary>
public static class ProtoTraceItemKeys
{
    /// <summary>Returns the snake_case segment for a CLR type, e.g. <c>InvoiceLine</c> to <c>invoice_line</c>.</summary>
    public static string TypeSegment(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ToSnakeCase(type.Name);
    }

    /// <summary>Returns the snake_case segment for a CLR type name, e.g. <c>InvoiceLine</c> to <c>invoice_line</c>.</summary>
    public static string TypeSegment(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        return ToSnakeCase(typeName);
    }

    /// <summary>
    /// Inserts <c>_</c> between a lowercase letter or digit and a following uppercase letter, and
    /// before the last capital of an acronym run that starts a word (<c>HTTPClient</c> to
    /// <c>http_client</c>). Repeated separators collapse, leading and trailing ones are trimmed, and
    /// the result is lowercased with the invariant culture.
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '_')
            {
                AddSeparator(builder);
                continue;
            }

            if (char.IsUpper(character) && index > 0 && NeedsSeparator(name, index))
            {
                AddSeparator(builder);
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        while (builder.Length > 0 && builder[^1] == '_')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    private static bool NeedsSeparator(string name, int index)
    {
        var previous = name[index - 1];
        if (char.IsLower(previous) || char.IsDigit(previous))
        {
            return true;
        }

        return char.IsUpper(previous)
            && index + 1 < name.Length
            && char.IsLower(name[index + 1]);
    }

    private static void AddSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '_')
        {
            builder.Append('_');
        }
    }
}
