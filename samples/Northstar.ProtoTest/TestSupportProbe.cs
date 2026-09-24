namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Core;
using global::ProtoTest.Rest;

/// <summary>
/// Verifies once per run that the application exposes <c>/test-support</c>, so a deployment that does
/// not enable the development affordance fails with a clear message instead of a 404 during scenario
/// provisioning. Registered as a run-scoped singleton: the probe result lives exactly as long as the
/// host that produced it, so a second run in the same process probes again.
/// </summary>
internal sealed class TestSupportProbe
{
    private bool _verified;

    public async Task EnsureAvailableAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_verified)
        {
            return;
        }

        using var response = await context.Rest().WithoutAuth().GetAsync("/test-support");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "The application does not expose /test-support, so scenario provisioning is unavailable. " +
                "Run it with ProtoTest:TestSupport=true (a development affordance), or point " +
                "the suite at an environment that enables it.");
        }

        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        _verified = true;
    }
}
