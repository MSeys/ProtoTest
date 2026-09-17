namespace ProtoTest.Web;

/// <summary>Semantic roles understood by ProtoTest web backends.</summary>
public enum WebRole
{
    Alert,
    Button,
    Checkbox,
    Combobox,
    Dialog,
    Grid,
    Heading,
    Image,
    Link,
    List,
    ListItem,
    Menu,
    MenuItem,
    Navigation,
    Option,
    ProgressBar,
    Radio,
    Region,
    Row,
    RowGroup,
    Searchbox,
    Slider,
    SpinButton,
    Status,
    Switch,
    Tab,
    Table,
    TabList,
    TabPanel,
    Textbox,
    Toolbar,
    Tooltip,
    Tree,
    TreeItem
}

/// <summary>A structured, backend-neutral description of how to find an element.</summary>
public abstract record WebLocator
{
    public WebLocator And(WebLocator condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new AndWebLocator(this, condition);
    }

    public abstract string Describe();
}

public sealed record TestIdWebLocator(string Value) : WebLocator
{
    public override string Describe() => $"TestId(\"{Value}\")";
}

public sealed record RoleWebLocator(WebRole Role, string? Name = null, bool Exact = true) : WebLocator
{
    public override string Describe() => Name is null ? $"Role({Role})" : $"Role({Role}, \"{Name}\", exact: {Exact.ToString().ToLowerInvariant()})";
}

public sealed record TextWebLocator(string Value, bool Exact = false, bool IgnoreCase = false) : WebLocator
{
    public override string Describe() => $"Text(\"{Value}\", exact: {Exact.ToString().ToLowerInvariant()}, ignoreCase: {IgnoreCase.ToString().ToLowerInvariant()})";
}

public sealed record LabelWebLocator(string Value, bool Exact = true) : WebLocator
{
    public override string Describe() => $"Label(\"{Value}\", exact: {Exact.ToString().ToLowerInvariant()})";
}

public sealed record PlaceholderWebLocator(string Value, bool Exact = true) : WebLocator
{
    public override string Describe() => $"Placeholder(\"{Value}\", exact: {Exact.ToString().ToLowerInvariant()})";
}

public sealed record CssWebLocator(string Selector) : WebLocator
{
    public override string Describe() => $"Css(\"{Selector}\")";
}

public sealed record AttributeWebLocator(string Name, string Value) : WebLocator
{
    public override string Describe() => $"Attribute(\"{Name}\", \"{Value}\")";
}

public sealed record HasTextWebLocator(string Value, bool Exact = false, bool IgnoreCase = false) : WebLocator
{
    public override string Describe() => $"HasText(\"{Value}\", exact: {Exact.ToString().ToLowerInvariant()}, ignoreCase: {IgnoreCase.ToString().ToLowerInvariant()})";
}

public sealed record AndWebLocator(WebLocator Left, WebLocator Right) : WebLocator
{
    public override string Describe() => $"{Left.Describe()}.And({Right.Describe()})";
}

public sealed record NthWebLocator(WebLocator Source, int Index) : WebLocator
{
    public override string Describe() => $"{Source.Describe()}.At({Index})";
}

public sealed record TableCellWebLocator(int Index) : WebLocator
{
    public override string Describe() => $"TableCellAt({Index})";
}

public sealed record TableCellByHeaderWebLocator(string Header, bool Exact = true, bool IgnoreCase = false) : WebLocator
{
    public override string Describe() =>
        $"TableCell(\"{Header}\", exact: {Exact.ToString().ToLowerInvariant()}, ignoreCase: {IgnoreCase.ToString().ToLowerInvariant()})";
}

/// <summary>Factory for semantic web locators.</summary>
public static class By
{
    public static WebLocator TestId(string value) => new TestIdWebLocator(Required(value));
    public static WebLocator Role(WebRole role, string? name = null, bool exact = true) => new RoleWebLocator(role, name, exact);
    public static WebLocator Text(string value, bool exact = false, bool ignoreCase = false) => new TextWebLocator(Required(value), exact, ignoreCase);
    public static WebLocator Label(string value, bool exact = true) => new LabelWebLocator(Required(value), exact);
    public static WebLocator Placeholder(string value, bool exact = true) => new PlaceholderWebLocator(Required(value), exact);
    public static WebLocator Css(string selector) => new CssWebLocator(Required(selector));
    public static WebLocator Attribute(string name, string value)
    {
        Required(name);
        if (!name.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':'))
            throw new ArgumentException("An attribute name may contain only letters, digits, '-', '_', and ':'.", nameof(name));
        return new AttributeWebLocator(name, Required(value));
    }
    public static WebLocator HasText(string value, bool exact = false, bool ignoreCase = false) => new HasTextWebLocator(Required(value), exact, ignoreCase);
    public static WebLocator At(WebLocator source, int index)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new NthWebLocator(source, index);
    }

    /// <summary>Addresses a zero-based cell within the current table row.</summary>
    public static WebLocator TableCellAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new TableCellWebLocator(index);
    }

    /// <summary>Addresses a one-based cell number within the current table row.</summary>
    public static WebLocator TableCellNumber(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return new TableCellWebLocator(number - 1);
    }

    /// <summary>Addresses a cell by the text of its conventional table header.</summary>
    public static WebLocator TableCell(string header, bool exact = true, bool ignoreCase = false)
        => new TableCellByHeaderWebLocator(Required(header), exact, ignoreCase);

    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
