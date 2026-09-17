namespace ProtoTest.Web;

/// <summary>
/// Shared XPath construction for web backends: literals, text predicates, and the
/// table-cell-by-header expression. Public so additional backend packages translate identically.
/// </summary>
public static class WebXPath
{
    /// <summary>Quotes <paramref name="value"/> as an XPath string literal, including mixed quotes.</summary>
    public static string Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains('\'')) return $"'{value}'";
        if (!value.Contains('"')) return $"\"{value}\"";
        return "concat(" + string.Join(",\"'\",", value.Split('\'').Select(part => $"'{part}'")) + ")";
    }

    /// <summary>Builds a <c>normalize-space(.)</c> comparison, optionally case-insensitive.</summary>
    public static string TextPredicate(string value, bool exact, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(value);
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        var expression = "normalize-space(.)";
        var expected = value;
        if (ignoreCase)
        {
            expression = $"translate({expression},'{upper}','{lower}')";
            expected = value.ToLowerInvariant();
        }

        return exact ? $"{expression}={Literal(expected)}" : $"contains({expression},{Literal(expected)})";
    }

    /// <summary>Builds the XPath for the cell under the column identified by a header cell.</summary>
    public static string TableCellByHeader(TableCellByHeaderWebLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var predicate = TextPredicate(locator.Header, locator.Exact, locator.IgnoreCase);
        var headers = $"ancestor::table[1]//tr[1]/*[self::th or self::td][{predicate}]";
        return $"./*[self::th or self::td][{headers} and position()=count({headers}/preceding-sibling::*[self::th or self::td])+1]";
    }
}
