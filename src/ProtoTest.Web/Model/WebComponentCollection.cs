namespace ProtoTest.Web;

/// <summary>A lazy collection of typed components selected inside a parent component.</summary>
public sealed class WebComponentCollection<TComponent> where TComponent : WebComponent, new()
{
    private readonly WebSession _session;
    private readonly ComponentScope _parent;
    private readonly WebLocator _items;
    private readonly string _name;

    internal WebComponentCollection(WebSession session, ComponentScope parent, WebLocator items, string name)
    {
        _session = session;
        _parent = parent;
        _items = items;
        _name = name;
    }

    public ValueTask<int> CountAsync(CancellationToken cancellationToken = default)
        => _session.CountAsync(Reference(_items, _name), cancellationToken);

    /// <summary>Returns a lazy component at a zero-based index.</summary>
    public TComponent At(int index, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return Create(By.At(_items, index), name ?? $"{_name}[{index}]");
    }

    /// <summary>Returns a lazy component by a one-based number, useful for legacy numbered UIs.</summary>
    public TComponent Number(int number, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return Create(By.At(_items, number - 1), name ?? $"{_name}[{number}]");
    }

    public TComponent First(string? name = null) => At(0, name ?? $"{_name}.First");

    /// <summary>Creates a lazy, strict component reference filtered by a semantic condition.</summary>
    public TComponent Matching(WebLocator condition, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Create(_items.And(condition), name ?? $"{_name}.Matching");
    }

    private TComponent Create(WebLocator root, string pathName)
    {
        var component = new TComponent();
        component.Initialize(
            _session,
            new ComponentScope([.. _parent.Roots, root], $"{_parent.Path}.{pathName}"));
        return component;
    }

    private WebElementReference Reference(WebLocator locator, string name)
        => new(_parent.Roots, _parent.Path, name, locator);
}
