namespace ProtoTest.Web.Internal;

/// <summary>
/// One semantic web operation's vocabulary: the trace kind its operation is recorded under, the kind
/// middlewares and wait conditions match, and the display verb its trace name uses. The session's
/// wrappers are thin because this lives here once.
/// </summary>
internal sealed record WebOperation(string TraceKind, WebOperationKind Kind, string Action)
{
    /// <summary>
    /// The trace name for one element. An operation with a per-call detail (the key a press sends, for
    /// example) names it after the verb, so the trace reads <c>Press Enter</c> rather than <c>Press</c>.
    /// </summary>
    public string TraceName(WebElementReference element, string? detail = null)
        => detail is null
            ? $"WEB · {Action} · {element.Name}"
            : $"WEB · {Action} {detail} · {element.Name}";
}

/// <summary>The semantic operations the web session exposes, with their trace vocabulary in one place.</summary>
internal static class WebOperations
{
    public static readonly WebOperation Click = new("web.click", WebOperationKind.Click, "Click");
    public static readonly WebOperation Fill = new("web.fill", WebOperationKind.Fill, "Fill");
    public static readonly WebOperation Check = new("web.check", WebOperationKind.Check, "Check");
    public static readonly WebOperation Uncheck = new("web.check", WebOperationKind.Check, "Uncheck");
    public static readonly WebOperation SelectOption = new("web.select_option", WebOperationKind.SelectOption, "Select option");
    public static readonly WebOperation Press = new("web.press", WebOperationKind.Press, "Press");
    public static readonly WebOperation Count = new("web.count", WebOperationKind.Count, "Count");
    public static readonly WebOperation ReadText = new("web.read_text", WebOperationKind.ReadText, "Read text");
    public static readonly WebOperation ReadValue = new("web.read_value", WebOperationKind.ReadValue, "Read value");
    public static readonly WebOperation IsVisible = new("web.is_visible", WebOperationKind.IsVisible, "Is visible");
    public static readonly WebOperation IsEnabled = new("web.is_enabled", WebOperationKind.IsEnabled, "Is enabled");
    public static readonly WebOperation IsChecked = new("web.is_checked", WebOperationKind.IsChecked, "Is checked");
}
