namespace ProtoTest.Web;

using ProtoTest.Core;

public enum WebLoginMode
{
    UserInterface,
    Api,
    StorageState
}

public sealed record WebLoginContext(
    ProtoExecutionContext Execution,
    WebSession Web,
    string Persona,
    WebLoginMode Mode);

/// <summary>Application-owned authentication strategy used by <see cref="LoginAsAttribute"/>.</summary>
public interface IWebLoginStrategy
{
    ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default);
}

/// <summary>Logs a named Web session in during test setup without storing credentials in attribute metadata.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class LoginAsAttribute(string persona, string session = "Default") : ProtoAttribute
{
    public string Persona { get; } = Required(persona);
    public string Session { get; } = Required(session);
    public WebLoginMode Mode { get; init; } = WebLoginMode.UserInterface;

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["web.session"] = Session,
            ["web.login.persona"] = Persona,
            ["web.login.mode"] = Mode.ToString()
        };
        using var operation = context.Trace.StartOperation(
            "web.login",
            $"Login · {Persona} [{Session}]",
            "ProtoTest.Web",
            ProtoTracePhase.Setup,
            attributes);
        try
        {
            var strategy = context.TryService<IWebLoginStrategy>() ?? throw new InvalidOperationException(
                $"{nameof(LoginAsAttribute)} requires an application-owned {nameof(IWebLoginStrategy)} registration.");
            await strategy.LoginAsync(new WebLoginContext(context, context.Web(Session), Persona, Mode));
            operation.Succeed();
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
