namespace ProtoTest.Web;

/// <summary>A cheap, reusable reference to an element; native resolution occurs per operation.</summary>
public sealed class WebElement
{
    private readonly WebSession _session;

    internal WebElement(WebSession session, ComponentScope scope, WebLocator locator, string name)
    {
        _session = session;
        Reference = new WebElementReference(scope.Roots, scope.Path, name, locator);
    }

    public WebElementReference Reference { get; }
    public string Name => Reference.Name;
    public string ComponentPath => Reference.ComponentPath;
    public WebLocator Locator => Reference.Locator;

    public ValueTask ClickAsync(CancellationToken cancellationToken = default)
        => _session.ClickAsync(Reference, cancellationToken);

    public ValueTask FillAsync(string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _session.FillAsync(Reference, value, cancellationToken);
    }

    public ValueTask<string> TextAsync(CancellationToken cancellationToken = default)
        => _session.ReadTextAsync(Reference, cancellationToken);

    public ValueTask<string?> ValueAsync(CancellationToken cancellationToken = default)
        => _session.ReadValueAsync(Reference, cancellationToken);

    public ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken = default)
        => _session.IsVisibleAsync(Reference, cancellationToken);

    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
        => _session.IsEnabledAsync(Reference, cancellationToken);

    public ValueTask<bool> IsCheckedAsync(CancellationToken cancellationToken = default)
        => _session.IsCheckedAsync(Reference, cancellationToken);

    public ValueTask CheckAsync(CancellationToken cancellationToken = default)
        => _session.CheckAsync(Reference, true, cancellationToken);

    public ValueTask UncheckAsync(CancellationToken cancellationToken = default)
        => _session.CheckAsync(Reference, false, cancellationToken);

    public ValueTask SelectOptionAsync(string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _session.SelectOptionAsync(Reference, value, cancellationToken);
    }

    public ValueTask PressAsync(WebKey key, CancellationToken cancellationToken = default)
        => _session.PressAsync(Reference, key, cancellationToken);

    public ValueTask ShouldBeVisibleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _session.ShouldBeVisibleAsync(Reference, timeout, cancellationToken);

    public ValueTask ShouldBeEnabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _session.ShouldBeEnabledAsync(Reference, timeout, cancellationToken);

    public ValueTask ShouldBeCheckedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _session.ShouldBeCheckedAsync(Reference, timeout, cancellationToken);

    public ValueTask ShouldHaveTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return _session.ShouldHaveTextAsync(Reference, expected, contains: false, timeout, cancellationToken);
    }

    public ValueTask ShouldContainTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return _session.ShouldHaveTextAsync(Reference, expected, contains: true, timeout, cancellationToken);
    }

    public ValueTask ShouldHaveValueAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return _session.ShouldHaveValueAsync(Reference, expected, timeout, cancellationToken);
    }
}
