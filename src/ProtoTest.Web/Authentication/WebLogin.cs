namespace ProtoTest.Web;

using ProtoTest.Core;

public sealed record WebLoginContext(
    ProtoExecutionContext Execution,
    WebSession Web,
    string Persona);

/// <summary>
/// An application-owned login flow applied by <see cref="LoginAsAttribute{TStrategy}"/>. Define one
/// implementation per distinct way your application logs a user in (driving the real login page,
/// calling an API and injecting a token, loading captured storage state, impersonation, ...); nothing
/// about ProtoTest constrains how many you have or what they're named.
/// </summary>
public interface IWebLoginStrategy
{
    ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Logs a named Web session in during test setup using an application-owned <typeparamref name="TStrategy"/>,
/// without storing credentials in attribute metadata.
/// Usage: [LoginAs&lt;AdminUiLogin&gt;("Administrator")] or [LoginAs&lt;ApiTokenLogin&gt;("Administrator", "tenant-a")]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class LoginAsAttribute<TStrategy>(string persona, params object[] constructorArgs) : ProtoAttribute
    where TStrategy : class, IWebLoginStrategy
{
    public string Persona { get; } = Required(persona);
    public string Session { get; init; } = "Default";

    public override Task BeforeTestAsync(ProtoExecutionContext context)
        => context.Trace
            .Operation("web.login", $"Login · {Persona} [{Session}]", "ProtoTest.Web")
            .During(ProtoTracePhase.Setup)
            .With("web.session", Session)
            .With("web.login.persona", Persona)
            .With("web.login.strategy", typeof(TStrategy).FullName)
            .RunAsync(async () =>
            {
                var strategy = ProtoAuthenticatorFactory.Create<TStrategy>(context, constructorArgs);
                await strategy.LoginAsync(new WebLoginContext(context, context.Web(Session), Persona));
            })
            .AsTask();

    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
