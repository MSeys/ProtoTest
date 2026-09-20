namespace ProtoTest.Web;

using ProtoTest.Core;

/// <summary>
/// Declares a named web session for the test during setup, created on demand like any other session.
/// Optionally navigates it to a start URL. Combine with <see cref="LoginAsAttribute{TStrategy}"/> when
/// the session also needs authentication; use <see cref="ProtoAttribute.Order"/> to sequence the two if needed.
/// </summary>
/// <example>
/// <code>
/// [WebSession("Admin", Open = "https://app.test/back-office")]
/// [WebSession("Customer")]
/// [LoginAs&lt;BackOfficeLogin&gt;("billing.admin", Session = "Admin")]
/// public async Task ...() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class WebSessionAttribute : ProtoAttribute
{
    public WebSessionAttribute(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A web session name is required.", nameof(name))
            : name;
        // Declarations run before logins (default Order 0), so a session is open before it is logged in.
        Order = -10;
    }

    /// <summary>Gets the session name, matching <c>Proto.Context.Web(name)</c> and <c>LoginAs(Session = ...)</c>.</summary>
    public string Name { get; }

    /// <summary>Gets or sets an optional absolute or relative URL to navigate this session to during setup.</summary>
    public string? Open { get; init; }

    /// <summary>
    /// Gets or sets the application this session targets. Defaults to
    /// <c>ProtoTest:Web:Sessions:{name}:Application</c>, then the session name.
    /// </summary>
    public string? Application { get; init; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var session = context.Web(Name, Application);

        // The whole URL can come from configuration, so code can stay environment-agnostic:
        // ProtoTest:Web:Sessions:{name}:Open overrides the attribute; a relative value resolves
        // against the session's BaseUrl (ProtoTest:Applications:{application}:BaseUrl). Settings
        // provided by started infrastructure win over the static configuration, like the session's
        // BaseUrl resolution.
        var key = $"ProtoTest:Web:Sessions:{Name}:Open";
        string? open = null;
        if (context.TryService<ProtoInfrastructureSettings>() is { } settings
            && settings.Values.TryGetValue(key, out var provided))
        {
            open = provided;
        }

        open ??= context.Configuration[key];
        if (string.IsNullOrWhiteSpace(open))
        {
            open = Open;
        }

        if (string.IsNullOrWhiteSpace(open))
        {
            return;
        }

        await session.NavigateAsync(new Uri(open, UriKind.RelativeOrAbsolute), CancellationToken.None);
    }
}
