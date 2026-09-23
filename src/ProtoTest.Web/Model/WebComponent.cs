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
        var scope = root is null ? Scope : Scope.WithRoot(root);
        return scope.Under(componentName).Create<TComponent>(Session);
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

    /// <summary>
    /// Runs <paramref name="trigger"/> and captures the download it starts, returning the file and
    /// registering it as a test attachment. A backend without download support throws
    /// <see cref="WebBackendCapabilityException"/> before the trigger runs.
    /// </summary>
    public ValueTask<WebDownload> DownloadAsync(
        Func<CancellationToken, Task> trigger,
        string? name = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => Web.DownloadAsync(trigger, name, timeout, cancellationToken);
}

/// <summary>
/// The scope a component resolves inside: the locator roots from its parents and the dotted path the
/// traces name it by. Creating a component goes through <see cref="Create{TComponent}"/>, so every
/// construction site builds the scope the same way.
/// </summary>
internal sealed record ComponentScope(IReadOnlyList<WebLocator> Roots, string Path)
{
    /// <summary>A scope one locator root deeper, such as a component addressed inside its parent.</summary>
    public ComponentScope WithRoot(WebLocator root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return this with { Roots = [.. Roots, root] };
    }

    /// <summary>A scope one path segment deeper, so a child's trace names its parent chain.</summary>
    public ComponentScope Under(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return this with { Path = $"{Path}.{name}" };
    }

    /// <summary>Creates and initializes a component in this scope.</summary>
    public TComponent Create<TComponent>(WebSession session) where TComponent : WebComponent, new()
    {
        var component = new TComponent();
        component.Initialize(session, this);
        return component;
    }
}
