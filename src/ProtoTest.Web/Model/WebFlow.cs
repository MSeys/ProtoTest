namespace ProtoTest.Web;

/// <summary>A typed, sequential interaction plan whose individual actions retain normal tracing and waits.</summary>
public sealed class WebFlow<TComponent> where TComponent : WebComponent
{
    private readonly TComponent _component;
    private readonly string _name;
    private readonly List<Func<CancellationToken, ValueTask>> _steps = [];
    private int _started;

    internal WebFlow(TComponent component, string name)
    {
        _component = component;
        _name = name;
    }

    public WebFlow<TComponent> Fill(Func<TComponent, WebElement> element, string value)
        => Add(element, target => target.FillAsync(value));

    public WebFlow<TComponent> Click(Func<TComponent, WebElement> element)
        => Add(element, target => target.ClickAsync());

    public WebFlow<TComponent> Check(Func<TComponent, WebElement> element, bool isChecked = true)
        => Add(element, target => isChecked ? target.CheckAsync() : target.UncheckAsync());

    public WebFlow<TComponent> Select(Func<TComponent, WebElement> element, string value)
        => Add(element, target => target.SelectOptionAsync(value));

    public WebFlow<TComponent> Press(Func<TComponent, WebElement> element, WebKey key)
        => Add(element, target => target.PressAsync(key));

    public WebFlow<TComponent> Do(Func<TComponent, CancellationToken, ValueTask> interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        EnsureNotStarted();
        _steps.Add(cancellationToken => interaction(_component, cancellationToken));
        return this;
    }

    public ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException($"Web flow '{_name}' can only run once.");
        return _component.OwningSession.RunFlowAsync(_name, _steps, cancellationToken);
    }

    private WebFlow<TComponent> Add(
        Func<TComponent, WebElement> select,
        Func<WebElement, ValueTask> interaction)
    {
        ArgumentNullException.ThrowIfNull(select);
        EnsureNotStarted();
        _steps.Add(_ => interaction(select(_component)));
        return this;
    }

    private void EnsureNotStarted()
    {
        if (Volatile.Read(ref _started) != 0)
            throw new InvalidOperationException($"Web flow '{_name}' has already started.");
    }
}

public static class WebFlowExtensions
{
    public static WebFlow<TComponent> Flow<TComponent>(this TComponent component, string name)
        where TComponent : WebComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new WebFlow<TComponent>(component, name);
    }

    public static ValueTask InteractAsync<TComponent>(
        this TComponent component,
        string name,
        Func<TComponent, ValueTask> interaction,
        CancellationToken cancellationToken = default)
        where TComponent : WebComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(interaction);
        return component.OwningSession.RunFlowAsync(
            name,
            [ct => { ct.ThrowIfCancellationRequested(); return interaction(component); }],
            cancellationToken);
    }
}
