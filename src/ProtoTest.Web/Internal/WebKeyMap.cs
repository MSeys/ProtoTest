namespace ProtoTest.Web.Internal;

/// <summary>
/// The one table that maps a semantic <see cref="WebKey"/> to each backend's native key notation.
/// Both backends read it, so the supported set cannot drift between them.
/// </summary>
internal static class WebKeyMap
{
    internal static readonly IReadOnlyDictionary<WebKey, WebKeyValue> Keys = new Dictionary<WebKey, WebKeyValue>
    {
        [WebKey.Enter] = new("Enter", "\uE007"),
        [WebKey.Tab] = new("Tab", "\uE004"),
        [WebKey.Escape] = new("Escape", "\uE00C"),
        [WebKey.Space] = new(" ", "\uE00D"),
        [WebKey.Backspace] = new("Backspace", "\uE003"),
        [WebKey.Delete] = new("Delete", "\uE017"),
        [WebKey.ArrowUp] = new("ArrowUp", "\uE013"),
        [WebKey.ArrowDown] = new("ArrowDown", "\uE015"),
        [WebKey.ArrowLeft] = new("ArrowLeft", "\uE012"),
        [WebKey.ArrowRight] = new("ArrowRight", "\uE014"),
        [WebKey.Home] = new("Home", "\uE011"),
        [WebKey.End] = new("End", "\uE010"),
        [WebKey.PageUp] = new("PageUp", "\uE00E"),
        [WebKey.PageDown] = new("PageDown", "\uE00F")
    };

    internal static WebKeyValue Get(WebKey key)
        => Keys.TryGetValue(key, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(key));
}

/// <summary>The native value each backend sends for one semantic key.</summary>
internal readonly record struct WebKeyValue(string Playwright, string Selenium);
