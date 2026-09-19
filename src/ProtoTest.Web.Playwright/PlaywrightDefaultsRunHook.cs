namespace ProtoTest.Web.Playwright;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Makes an application's code-configured Playwright defaults visible to the browser probe when the run
/// starts. The application builder cannot reach the host's configuration sources, so the same defaults
/// the host overload injects are registered against the host's configuration instead.
/// </summary>
internal sealed class PlaywrightDefaultsRunHook(
    IConfiguration configuration,
    PlaywrightWebOptions defaults) : IProtoRunHook
{
    public Task BeforeRunAsync(CancellationToken cancellationToken = default)
    {
        PlaywrightWebDefaults.Register(configuration, defaults);
        return Task.CompletedTask;
    }
}
