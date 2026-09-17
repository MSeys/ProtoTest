namespace ProtoTest.Web;

using System.Runtime.CompilerServices;

/// <summary>Base class for lazily scoped, reusable page components.</summary>
public abstract class WebComponent
{
    private WebSession? _session;
    private ComponentScope? _scope;

    internal void Initialize(WebSession session, ComponentScope scope)
    {
        if (_session is not null)
            throw new InvalidOperationException($"Component '{GetType().Name}' has already been initialized.");

        _session = session;
        _scope = scope;
    }

    protected WebElement Element(WebLocator locator, [CallerMemberName] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new WebElement(Session, Scope, locator, name ?? locator.Describe());
    }

    protected TComponent Component<TComponent>(
        WebLocator? root = null,
        [CallerMemberName] string? name = null)
        where TComponent : WebComponent, new()
    {
        var componentName = string.IsNullOrWhiteSpace(name) ? typeof(TComponent).Name : name;
        var roots = root is null ? Scope.Roots : [.. Scope.Roots, root];
        var component = new TComponent();
        component.Initialize(Session, new ComponentScope(roots, $"{Scope.Path}.{componentName}"));
        return component;
    }

    protected WebComponentCollection<TComponent> Components<TComponent>(
        WebLocator items,
        [CallerMemberName] string? name = null)
        where TComponent : WebComponent, new()
    {
        ArgumentNullException.ThrowIfNull(items);
        return new WebComponentCollection<TComponent>(
            Session,
            Scope,
            items,
            string.IsNullOrWhiteSpace(name) ? typeof(TComponent).Name : name);
    }

    protected WebSession Web => Session;

    internal WebSession OwningSession => Session;

    private WebSession Session => _session ?? throw new InvalidOperationException(
        $"Component '{GetType().Name}' must be created through Web.Page<T>() or Component<T>().");

    private ComponentScope Scope => _scope ?? throw new InvalidOperationException(
        $"Component '{GetType().Name}' has not been initialized.");
}

/// <summary>A top-level component with navigation support.</summary>
public abstract class WebPage : WebComponent
{
    public ValueTask OpenAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return Web.NavigateAsync(new Uri(address, UriKind.RelativeOrAbsolute), cancellationToken);
    }

    public ValueTask OpenAsync(Uri address, CancellationToken cancellationToken = default)
        => Web.NavigateAsync(address, cancellationToken);
}

internal sealed record ComponentScope(IReadOnlyList<WebLocator> Roots, string Path);
