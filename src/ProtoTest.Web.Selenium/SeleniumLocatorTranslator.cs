namespace ProtoTest.Web.Selenium;

using OpenQA.Selenium;
using SeleniumBy = OpenQA.Selenium.By;

internal static class SeleniumLocatorTranslator
{
    public static SeleniumBy Translate(WebLocator locator)
        => locator switch
        {
            CssWebLocator css => SeleniumBy.CssSelector(css.Selector),
            NthWebLocator nth => Translate(nth.Source),
            AndWebLocator and => SeleniumBy.XPath(Compound(and)),
            _ => SeleniumBy.XPath(XPath(locator))
        };

    internal static string DiagnosticSelector(WebLocator locator)
        => Translate(locator).ToString();

    private static string Compound(AndWebLocator locator)
    {
        if (locator.Left is CssWebLocator)
            throw new WebBackendCapabilityException("Selenium cannot apply semantic And() filters to a CSS escape-hatch locator.");
        if (locator.Right is not HasTextWebLocator text)
            throw new WebBackendCapabilityException("Selenium currently supports And() only with By.HasText().");
        return $"({XPath(locator.Left)})[{WebXPath.TextPredicate(text.Value, text.Exact, text.IgnoreCase)}]";
    }

    private static string XPath(WebLocator locator)
        => locator switch
        {
            TestIdWebLocator value => $".//*[@data-testid={WebXPath.Literal(value.Value)}]",
            RoleWebLocator value => Role(value),
            TextWebLocator value => $".//*[{WebXPath.TextPredicate(value.Value, value.Exact, value.IgnoreCase)}]",
            LabelWebLocator value => Label(value),
            PlaceholderWebLocator value => value.Exact
                ? $".//*[@placeholder={WebXPath.Literal(value.Value)}]"
                : $".//*[contains(@placeholder,{WebXPath.Literal(value.Value)})]",
            AttributeWebLocator value => $".//*[@{value.Name}={WebXPath.Literal(value.Value)}]",
            TableCellWebLocator value => $"./*[self::th or self::td][position()={value.Index + 1}]",
            TableCellByHeaderWebLocator value => WebXPath.TableCellByHeader(value),
            NthWebLocator value => XPath(value.Source),
            HasTextWebLocator => throw new WebBackendCapabilityException("HasText is a filter and must be composed with another locator using And()."),
            CssWebLocator => throw new WebBackendCapabilityException("CSS locators are translated directly and cannot be embedded in XPath."),
            AndWebLocator value => Compound(value),
            _ => throw new WebBackendCapabilityException($"Selenium does not support locator type '{locator.GetType().Name}'.")
        };

    private static string Role(RoleWebLocator locator)
    {
        var roleName = RoleName(locator.Role);
        var rolePredicate = locator.Role switch
        {
            WebRole.Button => "self::button or @role='button' or (self::input and (@type='button' or @type='submit' or @type='reset'))",
            WebRole.Link => "self::a[@href] or @role='link'",
            WebRole.Checkbox => "self::input[@type='checkbox'] or @role='checkbox'",
            WebRole.Radio => "self::input[@type='radio'] or @role='radio'",
            WebRole.Textbox => "self::textarea or (self::input and (not(@type) or @type='text' or @type='email' or @type='password' or @type='tel' or @type='url')) or @role='textbox'",
            WebRole.Heading => "self::h1 or self::h2 or self::h3 or self::h4 or self::h5 or self::h6 or @role='heading'",
            WebRole.Image => "self::img or @role='img'",
            _ => $"@role={WebXPath.Literal(roleName)}"
        };
        if (locator.Name is null) return $".//*[{rolePredicate}]";
        var name = WebXPath.Literal(locator.Name);
        var namePredicate = locator.Exact
            ? $"@aria-label={name} or @title={name} or @alt={name} or normalize-space(.)={name} or @value={name}"
            : $"contains(@aria-label,{name}) or contains(@title,{name}) or contains(@alt,{name}) or contains(normalize-space(.),{name}) or contains(@value,{name})";
        return $".//*[({rolePredicate}) and ({namePredicate})]";
    }

    private static string Label(LabelWebLocator locator)
    {
        var comparison = locator.Exact
            ? $"normalize-space(.)={WebXPath.Literal(locator.Value)}"
            : $"contains(normalize-space(.),{WebXPath.Literal(locator.Value)})";
        var ariaComparison = locator.Exact
            ? $"@aria-label={WebXPath.Literal(locator.Value)}"
            : $"contains(@aria-label,{WebXPath.Literal(locator.Value)})";
        return $".//*[{ariaComparison}] | " +
               $".//label[{comparison}]//*[self::input or self::textarea or self::select] | " +
               $".//*[@id=//label[{comparison}]/@for]";
    }

    private static string RoleName(WebRole role)
        => role switch
        {
            WebRole.ListItem => "listitem",
            WebRole.MenuItem => "menuitem",
            WebRole.ProgressBar => "progressbar",
            WebRole.RowGroup => "rowgroup",
            WebRole.SpinButton => "spinbutton",
            WebRole.TabList => "tablist",
            WebRole.TabPanel => "tabpanel",
            WebRole.TreeItem => "treeitem",
            WebRole.Image => "img",
            _ => role.ToString().ToLowerInvariant()
        };
}
