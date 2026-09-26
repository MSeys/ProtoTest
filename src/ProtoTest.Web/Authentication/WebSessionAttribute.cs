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
        // A session is declared before a login runs, so the login finds an open session.
        Order = ProtoAttributeOrder.SessionDeclaration;
    }

    /// <summary>Gets the session name, matching <c>Proto.Context.Web(name)</c> and <c>LoginAs(Session = ...)</c>.</summary>
    public string Name { get; }

    /// <summary>Gets or sets an optional absolute or relative URL to navigate this session to during setup.</summary>
    public string? Open { get; init; }

    /// <summary>
    /// Gets or sets the application this session targets, defaulting to the application selected for the
    /// test and then to the session name. Its address comes from
    /// <c>ProtoTest:Applications:{application}:BaseUrl</c>.
    /// </summary>
    public string? Application { get; init; }

    /// <summary>
    /// Gets or sets the application endpoint this session is rooted at, joined to the application's base
    /// URL through <c>ProtoTest:Applications:{application}:Endpoints:{endpoint}</c>.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets or sets whether Vue Router discovery records this session's routes as page inventory.</summary>
    public bool DiscoverRoutes { get; init; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var session = context.Web(Name, Application, Endpoint, DiscoverRoutes);

        // The start URL is code, resolved against the session's application address, so a relative value
        // stays environment-agnostic.
        if (string.IsNullOrWhiteSpace(Open))
        {
            return;
        }

        await session.NavigateAsync(new Uri(Open, UriKind.RelativeOrAbsolute), CancellationToken.None);
    }
}
