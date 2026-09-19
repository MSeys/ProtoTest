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

    /// <summary>
    /// Builds the XPath for the cell under the column identified by a header cell. A document-scoped
    /// lookup (Playwright's page, Selenium's driver) starts at the document node, which has no direct
    /// table cells, so its axis widens to <c>//</c>.
    /// </summary>
    public static string TableCellByHeader(TableCellByHeaderWebLocator locator, bool documentScoped = false)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var predicate = TextPredicate(locator.Header, locator.Exact, locator.IgnoreCase);
        var headers = $"ancestor::table[1]//tr[1]/*[self::th or self::td][{predicate}]";
        var axis = documentScoped ? "//" : "./";
        // The identity exclusion keeps the column's own header cells out of a document-wide search:
        // a header cell sits at exactly the position it defines, so without it the header matches its
        // own column lookup. XPath 1.0 has no node identity operator; the union count is the idiom.
        return $"{axis}*[self::th or self::td][{headers}" +
               $" and position()=count({headers}/preceding-sibling::*[self::th or self::td])+1" +
               $" and count(.|{headers})!=count({headers})]";
    }
}
