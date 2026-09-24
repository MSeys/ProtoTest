namespace ProtoTest.Web.Playwright;

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using ProtoTest.Web.Internal;

/// <summary>
/// Translates the semantic locators into Playwright's own locator API, against a page or inside a
/// component scope. One type owns the translation, so the same semantic locator resolves the same way
/// everywhere and the backend stays about operations rather than locator spelling.
/// </summary>
internal sealed class PlaywrightLocatorTranslator(IPage page)
{
    private readonly IPage _page = page ?? throw new ArgumentNullException(nameof(page));

    /// <summary>Resolves a semantic element reference through its component roots and its locator.</summary>
    public ILocator Resolve(WebElementReference element)
    {
        ILocator? current = null;
        foreach (var root in element.ComponentRoots)
        {
            current = Apply(current, root);
        }

        return Apply(current, element.Locator);
    }

    private ILocator Apply(ILocator? scope, WebLocator locator)
        => locator switch
        {
            TestIdWebLocator value => scope is null ? _page.GetByTestId(value.Value) : scope.GetByTestId(value.Value),
            RoleWebLocator value => Role(scope, value),
            TextWebLocator value => Text(scope, value),
            LabelWebLocator value => scope is null
                ? _page.GetByLabel(value.Value, new PageGetByLabelOptions { Exact = value.Exact })
                : scope.GetByLabel(value.Value, new LocatorGetByLabelOptions { Exact = value.Exact }),
            PlaceholderWebLocator value => scope is null
                ? _page.GetByPlaceholder(value.Value, new PageGetByPlaceholderOptions { Exact = value.Exact })
                : scope.GetByPlaceholder(value.Value, new LocatorGetByPlaceholderOptions { Exact = value.Exact }),
            CssWebLocator value => scope is null ? _page.Locator(value.Selector) : scope.Locator(value.Selector),
            AttributeWebLocator value => scope is null
                ? _page.Locator($"[{CssIdentifier(value.Name)}={CssString(value.Value)}]")
                : scope.Locator($"[{CssIdentifier(value.Name)}={CssString(value.Value)}]"),
            NthWebLocator value => Apply(scope, value.Source).Nth(value.Index),
            TableCellWebLocator value => (scope is null
                ? _page.Locator("th, td")
                : scope.Locator(":scope > th, :scope > td")).Nth(value.Index),
            TableCellByHeaderWebLocator value => scope is null
                ? _page.Locator($"xpath={WebXPath.TableCellByHeader(value, documentScoped: true)}")
                : scope.Locator($"xpath={WebXPath.TableCellByHeader(value)}"),
            AndWebLocator value => And(scope, value),
            HasTextWebLocator => throw new WebBackendCapabilityException("HasText is a filter and must be composed with another locator using And()."),
            _ => throw new WebBackendCapabilityException($"Playwright does not support locator type '{locator.GetType().Name}'.")
        };

    private ILocator Role(ILocator? scope, RoleWebLocator locator)
    {
        var role = MapRole(locator.Role);
        if (scope is null)
        {
            var options = new PageGetByRoleOptions { Exact = locator.Exact };
            if (locator.Name is not null) options.Name = locator.Name;
            return _page.GetByRole(role, options);
        }
        else
        {
            var options = new LocatorGetByRoleOptions { Exact = locator.Exact };
            if (locator.Name is not null) options.Name = locator.Name;
            return scope.GetByRole(role, options);
        }
    }

    private ILocator Text(ILocator? scope, TextWebLocator locator)
    {
        // A Playwright string is case-insensitive unless Exact; only the exact case-sensitive case can
        // use a plain string, the rest need a regex with matching flags.
        if (!locator.IgnoreCase && locator.Exact)
        {
            return scope is null
                ? _page.GetByText(locator.Value, new PageGetByTextOptions { Exact = true })
                : scope.GetByText(locator.Value, new LocatorGetByTextOptions { Exact = true });
        }

        var pattern = locator.Exact ? $"^{Regex.Escape(locator.Value)}$" : Regex.Escape(locator.Value);
        var regex = new Regex(pattern, locator.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return scope is null ? _page.GetByText(regex) : scope.GetByText(regex);
    }

    private ILocator And(ILocator? scope, AndWebLocator locator)
    {
        var left = Apply(scope, locator.Left);
        if (locator.Right is HasTextWebLocator text)
        {
            if (text.IgnoreCase && !text.Exact)
                return left.Filter(new LocatorFilterOptions { HasText = text.Value });
            var pattern = text.Exact ? $"^{Regex.Escape(text.Value)}$" : Regex.Escape(text.Value);
            return left.Filter(new LocatorFilterOptions
            {
                HasTextRegex = new Regex(pattern, text.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None)
            });
        }

        return left.And(Apply(scope, locator.Right));
    }

    internal static AriaRole MapRole(WebRole role) => role switch
    {
        WebRole.Alert => AriaRole.Alert,
        WebRole.Button => AriaRole.Button,
        WebRole.Checkbox => AriaRole.Checkbox,
        WebRole.Combobox => AriaRole.Combobox,
        WebRole.Dialog => AriaRole.Dialog,
        WebRole.Grid => AriaRole.Grid,
        WebRole.Heading => AriaRole.Heading,
        WebRole.Image => AriaRole.Img,
        WebRole.Link => AriaRole.Link,
        WebRole.List => AriaRole.List,
        WebRole.ListItem => AriaRole.Listitem,
        WebRole.Menu => AriaRole.Menu,
        WebRole.MenuItem => AriaRole.Menuitem,
        WebRole.Navigation => AriaRole.Navigation,
        WebRole.Option => AriaRole.Option,
        WebRole.ProgressBar => AriaRole.Progressbar,
        WebRole.Radio => AriaRole.Radio,
        WebRole.Region => AriaRole.Region,
        WebRole.Row => AriaRole.Row,
        WebRole.RowGroup => AriaRole.Rowgroup,
        WebRole.Searchbox => AriaRole.Searchbox,
        WebRole.Slider => AriaRole.Slider,
        WebRole.SpinButton => AriaRole.Spinbutton,
        WebRole.Status => AriaRole.Status,
        WebRole.Switch => AriaRole.Switch,
        WebRole.Tab => AriaRole.Tab,
        WebRole.Table => AriaRole.Table,
        WebRole.TabList => AriaRole.Tablist,
        WebRole.TabPanel => AriaRole.Tabpanel,
        WebRole.Textbox => AriaRole.Textbox,
        WebRole.Toolbar => AriaRole.Toolbar,
        WebRole.Tooltip => AriaRole.Tooltip,
        WebRole.Tree => AriaRole.Tree,
        WebRole.TreeItem => AriaRole.Treeitem,
        _ => throw new WebBackendCapabilityException($"Playwright role mapping is not available for '{role}'.")
    };

    /// <summary>The native value Playwright sends for each semantic key.</summary>
    internal static readonly IReadOnlyDictionary<WebKey, string> KeyMap = new Dictionary<WebKey, string>
    {
        [WebKey.Enter] = "Enter",
        [WebKey.Tab] = "Tab",
        [WebKey.Escape] = "Escape",
        [WebKey.Space] = " ",
        [WebKey.Backspace] = "Backspace",
        [WebKey.Delete] = "Delete",
        [WebKey.ArrowUp] = "ArrowUp",
        [WebKey.ArrowDown] = "ArrowDown",
        [WebKey.ArrowLeft] = "ArrowLeft",
        [WebKey.ArrowRight] = "ArrowRight",
        [WebKey.Home] = "Home",
        [WebKey.End] = "End",
        [WebKey.PageUp] = "PageUp",
        [WebKey.PageDown] = "PageDown"
    };

    internal static string MapKey(WebKey key)
        => KeyMap.TryGetValue(key, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(key));

    private static string CssIdentifier(string value)
    {
        if (value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':'))
            return value.Replace(":", "\\:");
        throw new WebBackendCapabilityException($"Attribute name '{value}' cannot be represented safely as a CSS identifier.");
    }

    private static string CssString(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
